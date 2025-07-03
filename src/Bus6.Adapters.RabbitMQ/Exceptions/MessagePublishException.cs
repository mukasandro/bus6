namespace Bus6.Adapters.RabbitMQ.Exceptions;

/// <summary>
/// Исключение, возникающее при ошибке публикации сообщения.
/// </summary>
public class MessagePublishException : Exception
{
    public MessagePublishException(string message, Exception innerException)
        : base(message, innerException) { }
}