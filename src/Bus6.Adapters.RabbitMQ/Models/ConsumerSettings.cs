namespace Bus6.Adapters.RabbitMQ.Models;

/// <summary>
/// Настройки для потребителя RabbitMQ.
/// </summary>
public class ConsumerSettings
{
    /// <summary>
    /// Имя очереди для прослушивания.
    /// </summary>
    public string QueueName { get; set; }

    /// <summary>
    /// Количество сообщений, которые могут быть получены до подтверждения (QoS).
    /// Определяет, сколько сообщений может одновременно находиться в обработке.
    /// </summary>
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>
    /// Флаг, указывающий, нужно ли автоматически подтверждать сообщения (auto-ack).
    /// Рекомендуется установить в false для надежной обработки сообщений.
    /// </summary>
    public bool AutoAck { get; set; } = false;
}
