using RabbitMQ.Client;

namespace Bus6.Adapters.RabbitMQ.Connection.Interfaces;

/// <summary>
/// Интерфейс для управления соединениями с RabbitMQ.
/// </summary>
public interface IRabbitMqConnectionManager : IDisposable
{
    /// <summary>
    /// Получает соединение для публикации сообщений.
    /// </summary>
    /// <returns>Соединение для публикации сообщений.</returns>
    Task<IConnection> GetPublishConnectionAsync();

    /// <summary>
    /// Получает соединение для потребления сообщений.
    /// </summary>
    /// <returns>Соединение для потребления сообщений.</returns>
    Task<IConnection> GetConsumeConnectionAsync();
}