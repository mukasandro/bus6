using Bus6.Adapters.RabbitMQ.Models;

namespace Bus6.Adapters.RabbitMQ.Consumers.Interfaces;

/// <summary>
/// Интерфейс для потребителя сообщений из RabbitMQ.
/// </summary>
public interface IRabbitMqConsumer : IAsyncDisposable
{
    /// <summary>
    /// Начинает прослушивание указанной очереди.
    /// </summary>
    /// <typeparam name="T">Тип сообщения для десериализации.</typeparam>
    /// <param name="settings">Настройки потребителя, включая имя очереди и QoS.</param>
    /// <param name="messageHandler">Асинхронный делегат для обработки полученного сообщения.</param>
    /// <param name="cancellationToken">Токен для отмены операции запуска.</param>
    Task StartConsumingAsync<T>(
        ConsumerSettings settings,
        Func<T, Task> messageHandler,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает прослушивание очереди.
    /// </summary>
    /// <remarks>
    /// Этот метод отменяет подписку потребителя, но не закрывает канал или соединение.
    /// </remarks>
    Task StopConsumingAsync();
}