using Bus6.Adapters.RabbitMQ.Configuration;
using Bus6.Adapters.RabbitMQ.Connection.Interfaces;
using Bus6.Adapters.RabbitMQ.Publishers;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;

namespace Bus6.Adapters.RabbitMQ.Tests.Publishers;

/// <summary>
/// Модульные тесты для RabbitMqPublisher.
/// </summary>
public class RabbitMqPublisherTests
{
    private readonly Mock<IRabbitMqConnectionManager> _connectionManagerMock;
    private readonly Mock<ILogger<RabbitMqPublisher>> _loggerMock;
    private readonly RabbitMqConfiguration _configuration;
    private readonly RabbitMqPublisher _publisher;

    public RabbitMqPublisherTests()
    {
        _connectionManagerMock = new Mock<IRabbitMqConnectionManager>();
        _loggerMock = new Mock<ILogger<RabbitMqPublisher>>();
        _configuration = new RabbitMqConfiguration();

        _publisher = new RabbitMqPublisher(
            _connectionManagerMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    [Fact]
    public void Constructor_WithNullConnectionManager_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqPublisher(null, _configuration, _loggerMock.Object));

        exception.ParamName.Should().Be("connectionManager");
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqPublisher(_connectionManagerMock.Object, null, _loggerMock.Object));

        exception.ParamName.Should().Be("configuration");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new RabbitMqPublisher(_connectionManagerMock.Object, _configuration, null));

        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public async Task PublishAsync_WithNullMessage_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _publisher.PublishAsync<string>(null, "test-exchange", "test.key"));

        exception.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task PublishAsync_WithEmptyExchange_ShouldThrowArgumentException()
    {
        // Arrange
        var message = new { Text = "Test message" };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _publisher.PublishAsync(message, "", "test.key"));

        exception.ParamName.Should().Be("exchange");
        exception.Message.Should().Contain("Exchange не может быть пустым");
    }

    [Fact]
    public async Task PublishAsync_WithValidMessage_ShouldCallConnectionManager()
    {
        // Arrange
        var message = new { Text = "Test message" };
        var mockConnection = new Mock<IConnection>();
        var mockChannel = new Mock<IChannel>();

        _connectionManagerMock
            .Setup(x => x.GetPublishConnectionAsync())
            .ReturnsAsync(mockConnection.Object);

        mockConnection
            .Setup(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockChannel.Object);

        // Act
        await _publisher.PublishAsync(message, "test-exchange", "test.key");

        // Assert
        _connectionManagerMock.Verify(x => x.GetPublishConnectionAsync(), Times.Once);
        mockConnection.Verify(x => x.CreateChannelAsync(It.IsAny<CreateChannelOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}