using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Bus6.Adapters.RabbitMQ.Configuration;
using Bus6.Adapters.RabbitMQ.Connection.Interfaces;
using Bus6.Adapters.RabbitMQ.Exceptions;
using RabbitMQ.Client.Exceptions;

namespace Bus6.Adapters.RabbitMQ.Connection;

/// <summary>
/// Простой менеджер соединений RabbitMQ без пулинга.
/// Управляет долгоживущими соединениями с учетом настроек ConnectionSettings.
/// </summary>
public sealed class RabbitMqConnectionManager :IRabbitMqConnectionManager, IDisposable
{
    private readonly RabbitMqConfiguration _config;
    private readonly ConnectionSettings _settings;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly object _lock = new();
    private readonly Timer _healthCheckTimer;

    private IConnection _publishConnection;
    private IConnection _consumeConnection;
    private DateTime _publishConnectionCreated;
    private DateTime _consumeConnectionCreated;
    private volatile bool _disposed;

    public RabbitMqConnectionManager(
        RabbitMqConfiguration config,
        ConnectionSettings settings,
        ILogger<RabbitMqConnectionManager> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Таймер проверки здоровья соединений, если включен мониторинг
        if (_settings.EnablePerformanceMonitoring)
        {
            _healthCheckTimer = new Timer(CheckConnectionHealth, null,
                TimeSpan.FromMilliseconds(_settings.HealthCheckInterval),
                TimeSpan.FromMilliseconds(_settings.HealthCheckInterval));
        }
    }

    /// <summary>
    /// Получает соединение для публикации сообщений.
    /// </summary>
    public async Task<IConnection> GetPublishConnectionAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RabbitMqConnectionManager));

        if (_settings.SeparateConnectionsForPublishAndConsume)
        {
            if (IsConnectionValid(_publishConnection, _publishConnectionCreated))
                return _publishConnection;

            _publishConnection = await GetOrCreateConnectionAsync("publish");
            _publishConnectionCreated = DateTime.UtcNow;
            return _publishConnection;
        }

        if (IsConnectionValid(_publishConnection, _publishConnectionCreated))
            return _publishConnection;

        _publishConnection = await GetOrCreateConnectionAsync("shared");
        _publishConnectionCreated = DateTime.UtcNow;
        return _publishConnection;
    }

    /// <summary>
    /// Получает соединение для потребления сообщений.
    /// </summary>
    public async Task<IConnection> GetConsumeConnectionAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RabbitMqConnectionManager));

        if (_settings.SeparateConnectionsForPublishAndConsume)
        {
            if (IsConnectionValid(_consumeConnection, _consumeConnectionCreated))
                return _consumeConnection;

            _consumeConnection = await GetOrCreateConnectionAsync("consume");
            _consumeConnectionCreated = DateTime.UtcNow;
            return _consumeConnection;
        }

        // Используем то же соединение, что и для публикации
        return await GetPublishConnectionAsync();
    }

    /// <summary>
    /// Создает новое соединение с проверкой старого.
    /// </summary>
    private async Task<IConnection> GetOrCreateConnectionAsync(string purpose)
    {
        IConnection existingConnection = purpose switch
        {
            "publish" or "shared" => _publishConnection,
            "consume" => _consumeConnection,
            _ => null
        };

        // Закрываем старое соединение если оно есть
        if (existingConnection != null)
        {
            try
            {
                existingConnection.Dispose();
                _logger.LogInformation("Закрыто устаревшее соединение для {Purpose}", purpose);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при закрытии устаревшего соединения");
            }
        }

        // Создаем новое соединение
        return await CreateConnectionAsync(purpose);
    }

    /// <summary>
    /// Проверяет валидность соединения с учетом настроек времени жизни.
    /// </summary>
    private bool IsConnectionValid(IConnection connection, DateTime created)
    {
        if (connection?.IsOpen != true)
            return false;

        // Проверка времени жизни соединения
        var age = DateTime.UtcNow - created;
        if (age.TotalMilliseconds > _settings.ConnectionLifetime)
        {
            _logger.LogDebug("Соединение превысило время жизни: {Age} > {Lifetime}ms",
                age, _settings.ConnectionLifetime);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Создает новое соединение с настройками автовосстановления.
    /// </summary>
    private async Task<IConnection> CreateConnectionAsync(string purpose, CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _config.HostName,
                Port = _config.Port,
                UserName = _config.UserName,
                Password = _config.Password,
                VirtualHost = _config.VirtualHost,
                AutomaticRecoveryEnabled = true,
                ClientProvidedName = $"Bus6-{purpose}-{Environment.MachineName}",
                // Используем значения из настроек
                RequestedHeartbeat = TimeSpan.FromSeconds(_settings.HeartbeatInterval),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(_settings.NetworkRecoveryInterval)
            };

            var connection = await factory.CreateConnectionAsync(cancellationToken);

            _logger.LogInformation("Создано соединение {Purpose} с RabbitMQ: {Host}:{Port}",
                purpose, _config.HostName, _config.Port);

            return connection;
        }
        catch (BrokerUnreachableException ex)
        {
            _logger.LogError(ex, "RabbitMQ недоступен для {Purpose}. Host: {Host}, Port: {Port}",
                purpose, _config.HostName, _config.Port);

            throw new RabbitMqConnectionException($"Не удалось подключиться к RabbitMQ для {purpose}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка создания соединения {Purpose} с RabbitMQ", purpose);
            throw new RabbitMqConnectionException($"Не удалось создать соединение {purpose} с RabbitMQ", ex);
        }
    }

    /// <summary>
    /// Периодическая проверка здоровья соединений.
    /// </summary>
    private void CheckConnectionHealth(object state)
    {
        if (_disposed) return;

        try
        {
            // Проверяем соединение для публикации
            if (_publishConnection != null && !_publishConnection.IsOpen)
            {
                _logger.LogWarning("Обнаружено закрытое соединение для публикации");
                lock (_lock)
                {
                    _publishConnection?.Dispose();
                    _publishConnection = null;
                }
            }

            // Проверяем соединение для потребления (если отдельное)
            if (_settings.SeparateConnectionsForPublishAndConsume &&
                _consumeConnection != null && !_consumeConnection.IsOpen)
            {
                _logger.LogWarning("Обнаружено закрытое соединение для потребления");
                lock (_lock)
                {
                    _consumeConnection?.Dispose();
                    _consumeConnection = null;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при проверке здоровья соединений");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _healthCheckTimer?.Dispose();

        try
        {
            _publishConnection?.Dispose();
            _consumeConnection?.Dispose();
            _logger.LogInformation("RabbitMqConnectionManager освобожден");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при закрытии соединений");
        }
    }
}