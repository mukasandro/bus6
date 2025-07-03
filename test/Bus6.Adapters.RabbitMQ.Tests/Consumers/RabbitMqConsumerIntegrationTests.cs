using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bus6.Adapters.RabbitMQ.Configuration;
using Bus6.Adapters.RabbitMQ.Connection;
using Bus6.Adapters.RabbitMQ.Consumers;
using Bus6.Adapters.RabbitMQ.Models;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;
using RabbitMqConfiguration = Bus6.Adapters.RabbitMQ.Configuration.RabbitMqConfiguration;

namespace Bus6.Adapters.RabbitMQ.Tests.Integration;

public class RabbitMqConsumerIntegrationTests : IAsyncLifetime
{
    private readonly RabbitMqContainer _rabbitMqContainer;
    private RabbitMqConfiguration _configuration;
    private RabbitMqConnectionManager _connectionManager;
    private RabbitMqConsumer _consumer;
    private IConnection _testConnection;
    private IChannel _testChannel;

    private const string Queue = "test-queue";
    private const string Exchange = "test-exchange";
    private const string RoutingKey = "test.key";

    public RabbitMqConsumerIntegrationTests()
    {
        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .WithUsername("test")
            .WithPassword("test")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5672))
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _rabbitMqContainer.StartAsync();

        _configuration = new RabbitMqConfiguration
        {
            HostName = _rabbitMqContainer.Hostname,
            Port = _rabbitMqContainer.GetMappedPublicPort(5672),
            UserName = "test",
            Password = "test",
            VirtualHost = "/"
        };

        var factory = new ConnectionFactory
        {
            HostName = _configuration.HostName,
            Port = _configuration.Port,
            UserName = _configuration.UserName,
            Password = _configuration.Password
        };

        _testConnection = await factory.CreateConnectionAsync();
        _testChannel = await _testConnection.CreateChannelAsync();

        await _testChannel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct, durable: true);
        await _testChannel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false);
        await _testChannel.QueueBindAsync(Queue, Exchange, RoutingKey);

        _connectionManager = new RabbitMqConnectionManager(_configuration, new ConnectionSettings(), NullLogger<RabbitMqConnectionManager>.Instance);
        _consumer = new RabbitMqConsumer(_connectionManager, NullLogger<RabbitMqConsumer>.Instance);
    }

    public async Task DisposeAsync()
    {
        await _consumer.DisposeAsync();
        _testChannel?.Dispose();
        _testConnection?.Dispose();
        _connectionManager?.Dispose();
        await _rabbitMqContainer.DisposeAsync();
    }

    private class TestMessage
    {
        public string Content { get; set; } = string.Empty;
    }
    
    

    [Fact]
    public async Task Consumer_Should_Receive_Published_Message()
    {
        // Arrange
        var expected = new TestMessage { Content = "Hello Test!" };
        var json = JsonSerializer.Serialize(expected, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var body = Encoding.UTF8.GetBytes(json);

        var receivedTcs = new TaskCompletionSource<TestMessage>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => receivedTcs.TrySetCanceled());

        var settings = new ConsumerSettings
        {
            QueueName = Queue,
            AutoAck = true,
            PrefetchCount = 1
        };

        await _consumer.StartConsumingAsync<TestMessage>(settings, msg =>
        {
            receivedTcs.SetResult(msg);
            return Task.CompletedTask;
        }, cts.Token);

        await _testChannel.BasicPublishAsync(Exchange, RoutingKey, body);

        // Assert
        var received = await receivedTcs.Task;
        received.Should().NotBeNull();
        received.Content.Should().Be(expected.Content);
    }
}