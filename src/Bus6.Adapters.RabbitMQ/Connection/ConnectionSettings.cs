namespace Bus6.Adapters.RabbitMQ.Connection;

/// <summary>
/// Настройки управления соединениями с RabbitMQ.
/// </summary>
public class ConnectionSettings
{
    /// <summary>
    /// Время жизни соединения в миллисекундах.
    /// </summary>
    public int ConnectionLifetime { get; set; } = 600000; // 10 минут
    
    /// <summary>
    /// Интервал проверки здоровья соединений в миллисекундах.
    /// </summary>
    public int HealthCheckInterval { get; set; } = 60000;
    
    /// <summary>
    /// Включить мониторинг производительности соединений.
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = false;

    /// <summary>
    /// Использовать отдельные соединения для публикации и потребления.
    /// </summary>
    public bool SeparateConnectionsForPublishAndConsume { get; set; } = true;

    /// <summary>
    /// Интервал heartbeat для соединений в секундах.
    /// </summary>
    public int HeartbeatInterval { get; set; } = 60;

    /// <summary>
    /// Интервал восстановления сети в секундах.
    /// </summary>
    public int NetworkRecoveryInterval { get; set; } = 5;
}