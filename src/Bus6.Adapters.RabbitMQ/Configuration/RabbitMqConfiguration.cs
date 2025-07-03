namespace Bus6.Adapters.RabbitMQ.Configuration;

/// <summary>
///  Конфигурация RabbitMQ для шины сообщений.
/// </summary>
public class RabbitMqConfiguration
{
    /// <summary>
    ///  Имя хоста RabbitMQ.
    /// </summary>
    public string HostName { get; set; } = "localhost";

    /// <summary>
    ///  Порт RabbitMQ.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    ///  Пользователь RabbitMQ.
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    ///  Пароль RabbitMQ.
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    ///  Виртуальный хост RabbitMQ.
    /// </summary>
    public string VirtualHost { get; set; } = "/";
}