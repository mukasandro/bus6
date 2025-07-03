using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Bus6.Adapters.RabbitMQ.Connection.Interfaces;
using Bus6.Adapters.RabbitMQ.Publishers.Interfaces;
using Bus6.Adapters.RabbitMQ.Exceptions;
using Bus6.Adapters.RabbitMQ.Configuration;

namespace Bus6.Adapters.RabbitMQ.Publishers;

/// <summary>
/// Публикатор сообщений для RabbitMQ.
/// Отвечает за отправку сообщений в указанные exchange с routing key.
/// </summary>
public class RabbitMqPublisher : IRabbitMqPublisher
{
    private readonly IRabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqConfiguration _configuration;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(
        IRabbitMqConnectionManager connectionManager,
        RabbitMqConfiguration configuration,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Публикует сообщение с использованием настроек по умолчанию.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <param name="message">Сообщение для публикации.</param>
    public async Task PublishAsync<T>(T message)
    {
        var exchange = GetDefaultExchange<T>();
        var routingKey = GetDefaultRoutingKey<T>();

        await PublishAsync(message, exchange, routingKey);
    }

    /// <summary>
    /// Публикует сообщение в указанный exchange с routing key.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <param name="message">Сообщение для публикации.</param>
    /// <param name="exchange">Имя exchange.</param>
    /// <param name="routingKey">Routing key.</param>
    public async Task PublishAsync<T>(T message, string exchange, string routingKey)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (string.IsNullOrWhiteSpace(exchange))
            throw new ArgumentException("Exchange не может быть пустым", nameof(exchange));

        try
        {
            // Получаем соединение для публикации
            var connection = await _connectionManager.GetPublishConnectionAsync();
            using var channel = await connection.CreateChannelAsync();

            // Объявляем exchange, если он не существует
            await channel.ExchangeDeclareAsync(
                exchange: exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false);

            // Сериализуем сообщение
            var messageBody = SerializeMessage(message);

            // Создаем свойства сообщения
            var properties = new BasicProperties
            {
                Persistent = true, // Делаем сообщение persistent
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                MessageId = Guid.NewGuid().ToString(),
                Type = typeof(T).Name
            };

            // Публикуем сообщение
            await channel.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey ?? string.Empty,
                mandatory: false,
                basicProperties: properties,
                body: messageBody);

            _logger.LogDebug("Сообщение {MessageType} опубликовано в exchange {Exchange} с routing key {RoutingKey}",
                typeof(T).Name, exchange, routingKey);
        }
        catch (Exception ex)
        {
            // Логируем ошибку
            _logger.LogError(ex, "Ошибка публикации сообщения {MessageType} в exchange {Exchange}",
                typeof(T).Name, exchange);

            // Выбрасываем пользовательское исключение
            throw new MessagePublishException(
                $"Не удалось опубликовать сообщение {typeof(T).Name} в exchange {exchange}", ex);
        }
    }

    /// <summary>
    /// Сериализует сообщение в JSON byte array.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <param name="message">Сообщение для сериализации.</param>
    /// <returns>Массив байтов сериализованного сообщения.</returns>
    private static byte[] SerializeMessage<T>(T message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Получает имя exchange по умолчанию для типа сообщения.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <returns>Имя exchange.</returns>
    private static string GetDefaultExchange<T>()
    {
        return $"bus6.{typeof(T).Name.ToLowerInvariant()}";
    }

    /// <summary>
    /// Получает routing key по умолчанию для типа сообщения.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <returns>Routing key.</returns>
    private static string GetDefaultRoutingKey<T>()
    {
        return typeof(T).Name.ToLowerInvariant();
    }
}