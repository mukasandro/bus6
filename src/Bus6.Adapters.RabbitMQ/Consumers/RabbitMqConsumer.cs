using System.Text;
using System.Text.Json;
using Bus6.Adapters.RabbitMQ.Connection.Interfaces;
using Bus6.Adapters.RabbitMQ.Consumers.Interfaces;
using Bus6.Adapters.RabbitMQ.Exceptions;
using Bus6.Adapters.RabbitMQ.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Bus6.Adapters.RabbitMQ.Consumers;

/// <summary>
/// Потребитель сообщений из RabbitMQ.
/// Отвечает за подписку на очередь, получение и обработку сообщений.
/// </summary>
public class RabbitMqConsumer : IRabbitMqConsumer
{
    // Менеджер для получения соединений с RabbitMQ.
    private readonly IRabbitMqConnectionManager _connectionManager;
    // Логгер для записи информации о работе потребителя.
    private readonly ILogger<RabbitMqConsumer> _logger;
    // Семафор для обеспечения потокобезопасности при изменении состояния потребителя (запуск/остановка).
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    // Канал для взаимодействия с RabbitMQ.
    private IChannel? _channel;
    // Уникальный тег, идентифицирующий потребителя на сервере RabbitMQ.
    private string? _consumerTag;
    // Флаг, указывающий, активен ли потребитель в данный момент.
    private bool _isConsuming;

    // Статические настройки для сериализации/десериализации JSON.
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        // Используем camelCase для имен свойств, что является стандартом для JSON.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="RabbitMqConsumer"/>.
    /// </summary>
    /// <param name="connectionManager">Менеджер соединений RabbitMQ.</param>
    /// <param name="logger">Логгер.</param>
    public RabbitMqConsumer(
        IRabbitMqConnectionManager connectionManager,
        ILogger<RabbitMqConsumer> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Начинает прослушивание указанной очереди для получения сообщений.
    /// </summary>
    /// <typeparam name="T">Тип сообщения, в который будет десериализовано тело сообщения.</typeparam>
    /// <param name="settings">Настройки потребителя, такие как имя очереди и QoS.</param>
    /// <param name="messageHandler">Обработчик, который будет вызван для каждого полученного сообщения.</param>
    /// <param name="cancellationToken">Токен для отмены операции.</param>
    public async Task StartConsumingAsync<T>(
        ConsumerSettings settings,
        Func<T, Task> messageHandler,
        CancellationToken cancellationToken = default)
    {
        // Проверка входных аргументов на null.
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (messageHandler == null) throw new ArgumentNullException(nameof(messageHandler));

        // Ожидаем освобождения семафора для безопасного изменения состояния.
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            // Если потребитель уже запущен, выходим.
            if (_isConsuming)
            {
                _logger.LogWarning("Потребитель уже запущен для очереди '{QueueName}'", settings.QueueName);
                return;
            }

            _logger.LogInformation("Запуск потребителя для очереди '{QueueName}'", settings.QueueName);

            // Получаем соединение и создаем канал.
            var connection = await _connectionManager.GetConsumeConnectionAsync();
            _channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

            // Настраиваем Quality of Service (QoS) для канала.
            // PrefetchCount ограничивает количество сообщений, которые сервер доставит потребителю без подтверждения.
            await _channel.BasicQosAsync(0, settings.PrefetchCount, false, cancellationToken);

            // Создаем асинхронного потребителя.
            var consumer = new AsyncEventingBasicConsumer(_channel);

            // Подписываемся на событие получения сообщения.
            consumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    // Десериализуем тело сообщения из JSON в объект типа T.
                    var body = ea.Body.ToArray();
                    var message = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(body), _jsonOptions);

                    if (message != null)
                    {
                        // Вызываем внешний обработчик сообщения.
                        await messageHandler(message);
                    }

                    // Если автоматическое подтверждение отключено, отправляем подтверждение вручную.
                    if (!settings.AutoAck && _channel.IsOpen)
                    {
                        await _channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке сообщения из очереди '{QueueName}'", settings.QueueName);
                    // Если произошла ошибка и авто-подтверждение выключено, отправляем отрицательное подтверждение.
                    // Сообщение будет возвращено в очередь или отправлено в Dead Letter Exchange.
                    if (!settings.AutoAck && _channel.IsOpen)
                    {
                        await _channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
                    }
                }
            };

            // Начинаем потребление сообщений из указанной очереди.
            _consumerTag = await _channel.BasicConsumeAsync(settings.QueueName, settings.AutoAck, consumer, cancellationToken);
            _isConsuming = true;

            _logger.LogInformation("Потребитель успешно запущен для очереди '{QueueName}' с тегом '{ConsumerTag}'", settings.QueueName, _consumerTag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось запустить потребителя для очереди '{QueueName}'", settings.QueueName);
            // В случае ошибки оборачиваем ее в наше кастомное исключение.
            throw new RabbitMqConnectionException($"Не удалось запустить потребителя для очереди '{settings.QueueName}'.", ex);
        }
        finally
        {
            // Освобождаем семафор.
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Останавливает прослушивание очереди.
    /// </summary>
    public async Task StopConsumingAsync()
    {
        // Ожидаем освобождения семафора для безопасного изменения состояния.
        await _stateLock.WaitAsync();
        try
        {
            // Если потребитель не запущен или тег отсутствует, делать нечего.
            if (!_isConsuming || _consumerTag == null)
            {
                return;
            }

            _logger.LogInformation("Остановка потребителя с тегом '{ConsumerTag}'...", _consumerTag);

            // Если канал открыт, отменяем подписку.
            if (_channel is { IsOpen: true })
            {
                await _channel.BasicCancelAsync(_consumerTag);
            }

            // Сбрасываем состояние.
            _consumerTag = null;
            _isConsuming = false;
            _logger.LogInformation("Потребитель остановлен");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при остановке потребителя");
        }
        finally
        {
            // Освобождаем семафор.
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Освобождает ресурсы, используемые потребителем.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        // Останавливаем потребителя, если он был запущен.
        await StopConsumingAsync();

        // Закрываем канал, если он был создан и открыт.
        if (_channel != null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        // Освобождаем семафор.
        _stateLock.Dispose();
        // Подавляем финализацию, так как ресурсы уже освобождены.
        GC.SuppressFinalize(this);
    }
}