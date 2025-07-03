namespace Bus6.Adapters.RabbitMQ.Exceptions;

/// <summary>
/// Исключение, возникающее при ошибке потребления сообщения.
/// </summary>
public class MessageConsumeException : Exception
{
    public MessageConsumeException(string message, Exception innerException)
        : base(message, innerException) { }
}