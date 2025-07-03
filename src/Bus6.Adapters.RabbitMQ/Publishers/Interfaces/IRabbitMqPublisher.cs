namespace Bus6.Adapters.RabbitMQ.Publishers.Interfaces;

/// <summary>
/// Интерфейс для публикации сообщений в RabbitMQ.
/// </summary>
public interface IRabbitMqPublisher
{
    /// <summary>
    /// Публикует сообщение с настройками по умолчанию.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <param name="message">Сообщение для публикации.</param>
    Task PublishAsync<T>(T message);

    /// <summary>
    /// Публикует сообщение в указанный exchange с routing key.
    /// </summary>
    /// <typeparam name="T">Тип сообщения.</typeparam>
    /// <param name="message">Сообщение для публикации.</param>
    /// <param name="exchange">Имя exchange.</param>
    /// <param name="routingKey">Routing key.</param>
    Task PublishAsync<T>(T message, string exchange, string routingKey);
}