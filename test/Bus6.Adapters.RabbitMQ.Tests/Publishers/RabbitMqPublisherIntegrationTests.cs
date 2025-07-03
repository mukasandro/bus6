using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Xunit;
using FluentAssertions;
using Bus6.Adapters.RabbitMQ.Configuration;
using Bus6.Adapters.RabbitMQ.Connection;
using Bus6.Adapters.RabbitMQ.Publishers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.RabbitMq;
using DotNet.Testcontainers.Builders;

namespace Bus6.Adapters.RabbitMQ.Tests.Integration;

/// <summary>
/// Интеграционные тесты для RabbitMqPublisher с использованием Testcontainers.
/// </summary>
public class RabbitMqPublisherIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer;
    private Bus6.Adapters.RabbitMQ.Configuration.RabbitMqConfiguration _configuration;
    private RabbitMqConnectionManager _connectionManager;
    private RabbitMqPublisher _publisher;
    private const string RabbitUser = "test";
    private const string RabbitPassword = "test";

    private IConnection _testConnection;
    private IChannel _testChannel;

    public RabbitMqPublisherIntegrationTests()
    {
        // Создаем контейнер RabbitMQ для тестов
        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .WithPortBinding(5672, true)
            .WithPortBinding(15672, true)
            .WithUsername(RabbitUser)
            .WithPassword(RabbitPassword)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
            .Build();
    }

    public async Task InitializeAsync()
    {
        // Запускаем контейнер и ждем готовности
        await _rabbitMqContainer.StartAsync();

        // Настраиваем конфигурацию для подключения к контейнеру
        _configuration = new Bus6.Adapters.RabbitMQ.Configuration.RabbitMqConfiguration
        {
            HostName = _rabbitMqContainer.Hostname,
            Port = _rabbitMqContainer.GetMappedPublicPort(5672),
            UserName = RabbitUser,
            Password = RabbitPassword,
            VirtualHost = "/"
        };

        var connectionSettings = new ConnectionSettings
        {
            EnablePerformanceMonitoring = false,
            SeparateConnectionsForPublishAndConsume = false,
            ConnectionLifetime = 60000,
            HealthCheckInterval = 30000,
            HeartbeatInterval = 60,
            NetworkRecoveryInterval = 10
        };

        var publisherLogger = NullLogger<RabbitMqPublisher>.Instance;
        var connectionLogger = NullLogger<RabbitMqConnectionManager>.Instance;

        _connectionManager = new RabbitMqConnectionManager(_configuration, connectionSettings, connectionLogger);
        _publisher = new RabbitMqPublisher(_connectionManager, _configuration, publisherLogger);

        // Создаем тестовое соединение для проверки сообщений
        var factory = new ConnectionFactory
        {
            HostName = _configuration.HostName,
            Port = _configuration.Port,
            UserName = _configuration.UserName,
            Password = _configuration.Password,
            VirtualHost = _configuration.VirtualHost
        };

        _testConnection = await factory.CreateConnectionAsync();
        _testChannel = await _testConnection.CreateChannelAsync();

        // Объявляем тестовые exchange и очереди
        await _testChannel.ExchangeDeclareAsync("test-exchange", ExchangeType.Topic, true, false);
        await _testChannel.QueueDeclareAsync("test-queue", true, false, false);
        await _testChannel.QueueBindAsync("test-queue", "test-exchange", "test.key");
    }

    public async Task DisposeAsync()
    {
        // Закрываем соединения
        if (_testChannel != null)
            await _testChannel.CloseAsync();
        if (_testConnection != null)
            await _testConnection.CloseAsync();

        _connectionManager?.Dispose();

        // Останавливаем контейнер
        await _rabbitMqContainer.StopAsync();
        await _rabbitMqContainer.DisposeAsync();
    }

    [Fact]
    public async Task PublishAsync_ShouldSendMessageToRealRabbitMq()
    {
        // Arrange
        var message = new TestMessage { Text = "Тестовое сообщение", Id = 123 };
        string receivedMessage = null;
        var messageReceived = new TaskCompletionSource<bool>();

        var consumer = new AsyncEventingBasicConsumer(_testChannel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            receivedMessage = Encoding.UTF8.GetString(ea.Body.Span);
            messageReceived.SetResult(true);
            await Task.CompletedTask;
        };

        await _testChannel.BasicConsumeAsync("test-queue", true, consumer);

        // Act - отправляем сообщение через publisher
        await _publisher.PublishAsync(message, "test-exchange", "test.key");

        // Assert - ждем получения сообщения
        var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().BeTrue();
        receivedMessage.Should().NotBeNull();

        var deserializedMessage = JsonSerializer.Deserialize<TestMessage>(receivedMessage, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        deserializedMessage.Should().NotBeNull();
        deserializedMessage.Text.Should().Be("Тестовое сообщение");
        deserializedMessage.Id.Should().Be(123);
    }

    [Fact]
    public async Task PublishAsync_WithDefaultSettings_ShouldCreateExchangeAndSendMessage()
    {
        // Arrange
        var message = new TestMessage { Text = "Сообщение с настройками по умолчанию", Id = 456 };

        // Создаем очередь для сообщений по умолчанию
        var defaultExchange = "bus6.testmessage";
        var defaultRoutingKey = "testmessage";

        await _testChannel.ExchangeDeclareAsync(defaultExchange, ExchangeType.Topic, true, false);
        await _testChannel.QueueDeclareAsync("default-test-queue", true, false, false);
        await _testChannel.QueueBindAsync("default-test-queue", defaultExchange, defaultRoutingKey);

        string receivedMessage = null;
        var messageReceived = new TaskCompletionSource<bool>();

        var consumer = new AsyncEventingBasicConsumer(_testChannel);
        consumer.ReceivedAsync += async (model, ea) =>
        {
            receivedMessage = Encoding.UTF8.GetString(ea.Body.Span);
            messageReceived.SetResult(true);
            await Task.CompletedTask;
        };

        await _testChannel.BasicConsumeAsync("default-test-queue", true, consumer);

        // Act - используем перегрузку с настройками по умолчанию
        await _publisher.PublishAsync(message);

        // Assert
        var received = await messageReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().BeTrue();
        receivedMessage.Should().NotBeNull();

        var deserializedMessage = JsonSerializer.Deserialize<TestMessage>(receivedMessage, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        deserializedMessage.Should().NotBeNull();
        deserializedMessage.Text.Should().Be("Сообщение с настройками по умолчанию");
        deserializedMessage.Id.Should().Be(456);
    }

    /// <summary>
    /// Тестовый класс для сообщений.
    /// </summary>
    public class TestMessage
    {
        public string Text { get; set; }
        public int Id { get; set; }
    }
}