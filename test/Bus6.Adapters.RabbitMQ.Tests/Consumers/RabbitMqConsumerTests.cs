using System;
using System.Threading;
using System.Threading.Tasks;
using Bus6.Adapters.RabbitMQ.Connection.Interfaces;
using Bus6.Adapters.RabbitMQ.Consumers;
using Bus6.Adapters.RabbitMQ.Exceptions;
using Bus6.Adapters.RabbitMQ.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;

namespace Bus6.Adapters.RabbitMQ.Tests.Consumers;

/// <summary>
/// Модульные тесты для RabbitMqConsumer.
/// </summary>
public class RabbitMqConsumerTests
{
    private readonly Mock<IRabbitMqConnectionManager> _mockConnectionManager;
    private readonly Mock<ILogger<RabbitMqConsumer>> _mockLogger;
    private readonly Mock<IConnection> _mockConnection;
    private readonly Mock<IChannel> _mockChannel;
    private readonly RabbitMqConsumer _consumer;

    public RabbitMqConsumerTests()
    {
        _mockConnectionManager = new Mock<IRabbitMqConnectionManager>();
        _mockLogger = new Mock<ILogger<RabbitMqConsumer>>();
        _mockConnection = new Mock<IConnection>();
        _mockChannel = new Mock<IChannel>();

        _consumer = new RabbitMqConsumer(_mockConnectionManager.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task StartConsumingAsync_WithValidSettings_ShouldStartConsuming()
    {
        // Arrange - настройка тестовых данных
        var settings = new ConsumerSettings
        {
            QueueName = "test-queue",
            AutoAck = false,
            PrefetchCount = 1
        };

        _mockConnectionManager.Setup(x => x.GetConsumeConnectionAsync())
            .ReturnsAsync(_mockConnection.Object);

        _mockConnection.Setup(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);

        _mockChannel.Setup(x => x.BasicQosAsync(It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockChannel.Setup(x => x.BasicConsumeAsync(
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<System.Collections.Generic.IDictionary<string, object>>(),
            It.IsAny<IAsyncBasicConsumer>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("consumer-tag");

        var messageHandler = new Func<TestMessage, Task>(msg => Task.CompletedTask);
        var cancellationToken = CancellationToken.None;

        // Act - выполнение тестируемого действия
        await _consumer.StartConsumingAsync(settings, messageHandler, cancellationToken);

        // Assert - проверка результатов
        _mockConnectionManager.Verify(x => x.GetConsumeConnectionAsync(), Times.Once);
        _mockConnection.Verify(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockChannel.Verify(x => x.BasicQosAsync(It.IsAny<uint>(), It.IsAny<ushort>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartConsumingAsync_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange - настройка тестовых данных
        var messageHandler = new Func<TestMessage, Task>(msg => Task.CompletedTask);

        // Act & Assert - выполнение и проверка исключения
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _consumer.StartConsumingAsync(null, messageHandler, CancellationToken.None));
    }

    [Fact]
    public async Task StartConsumingAsync_WithNullMessageHandler_ShouldThrowArgumentNullException()
    {
        // Arrange - настройка тестовых данных
        var settings = new ConsumerSettings { QueueName = "test-queue" };

        // Act & Assert - выполнение и проверка исключения
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _consumer.StartConsumingAsync<TestMessage>(settings, null, CancellationToken.None));
    }

    [Fact]
    public async Task StartConsumingAsync_WhenConnectionFails_ShouldThrowRabbitMqConnectionException()
    {
        // Arrange - настройка тестовых данных
        var settings = new ConsumerSettings { QueueName = "test-queue" };
        var messageHandler = new Func<TestMessage, Task>(msg => Task.CompletedTask);

        _mockConnectionManager.Setup(x => x.GetConsumeConnectionAsync())
            .ThrowsAsync(new Exception("Ошибка подключения"));

        // Act & Assert - выполнение и проверка исключения
        await Assert.ThrowsAsync<RabbitMqConnectionException>(() =>
            _consumer.StartConsumingAsync(settings, messageHandler, CancellationToken.None));
    }

    [Fact]
    public async Task StopConsumingAsync_WhenConsumerNotRunning_ShouldNotThrow()
    {
        // Act & Assert - не должно выбрасывать исключение, если потребитель не запущен
        await _consumer.StopConsumingAsync();
    }

    [Fact]
    public async Task StartConsumingAsync_WithCancelledToken_ShouldThrowTaskCanceledException()
    {
        // Arrange - настройка тестовых данных с отмененным токеном
        var settings = new ConsumerSettings { QueueName = "test-queue" };
        var messageHandler = new Func<TestMessage, Task>(msg => Task.CompletedTask);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - проверка исключения при отмененном токене
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            _consumer.StartConsumingAsync(settings, messageHandler, cts.Token));
    }

    [Fact]
    public async Task StartConsumingAsync_WithAutoAck_ShouldSetAutoAckTrue()
    {
        // Arrange - настройка тестовых данных с автоподтверждением
        var settings = new ConsumerSettings
        {
            QueueName = "test-queue",
            AutoAck = true,
            PrefetchCount = 1
        };

        _mockConnectionManager.Setup(x => x.GetConsumeConnectionAsync())
            .ReturnsAsync(_mockConnection.Object);
        _mockConnection.Setup(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockChannel.Object);

        var messageHandler = new Func<TestMessage, Task>(msg => Task.CompletedTask);

        // Act - запуск потребителя с автоподтверждением
        await _consumer.StartConsumingAsync(settings, messageHandler, CancellationToken.None);

        // Assert - проверка настройки автоподтверждения
        _mockChannel.Verify(x => x.BasicConsumeAsync(
            settings.QueueName,
            settings.AutoAck,
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<System.Collections.Generic.IDictionary<string, object>>(),
            It.IsAny<AsyncEventingBasicConsumer>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Тестовый класс сообщения для тестов
    /// </summary>
    private class TestMessage
    {
        public string Content { get; set; } = string.Empty;
        public int Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}