namespace Bus6.Adapters.RabbitMQ.Exceptions;

/// <summary>
/// Исключение, возникающее при ошибке подключения к RabbitMQ.
/// </summary>
public class RabbitMqConnectionException : Exception
{
    public RabbitMqConnectionException(string message, Exception innerException)
        : base(message, innerException) { }
}