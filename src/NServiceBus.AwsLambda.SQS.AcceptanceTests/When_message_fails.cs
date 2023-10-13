namespace NServiceBus.AcceptanceTests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using System.Linq;

class When_message_fails : AwsLambdaSQSEndpointTestBase
{
    [Test]
    public async Task Should_move_message_to_error_queue()
    {
        // the prefix will be configured using the transport's prefix configuration therefore we remove it for the endpoint name
        var endpointName = QueueName;

        var receivedMessages = await GenerateAndReceiveSQSEvent<TestMessage>();

        var context = new TestContext();

        var endpoint = new AwsLambdaSQSEndpoint(ctx =>
        {
            var configuration = new AwsLambdaSQSEndpointConfiguration(endpointName, CreateSQSClient(), CreateSNSClient());

            var advanced = configuration.AdvancedConfiguration;
            advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
            advanced.Recoverability().Immediate(i => i.NumberOfRetries(0));

            advanced.SendFailedMessagesTo(ErrorQueueName);

            return configuration;
        });

        Assert.DoesNotThrowAsync(() => endpoint.Process(receivedMessages, null), "message should be moved to the error queue instead");

        var errorMessages = await RetrieveMessagesInErrorQueue();
        Assert.AreEqual(1, errorMessages.Records.Count);
        JsonDocument errorMessage = JsonSerializer.Deserialize<JsonDocument>(errorMessages.Records.First().Body);
        var errorMessageHeader = errorMessage.RootElement.GetProperty("Headers");
        Assert.AreEqual("simulated exception", errorMessageHeader.GetProperty("NServiceBus.ExceptionInfo.Message").GetString());
        Assert.AreEqual(endpointName, errorMessageHeader.GetProperty("NServiceBus.ProcessingEndpoint").GetString());
        Assert.AreEqual(QueueName, errorMessageHeader.GetProperty("NServiceBus.FailedQ").GetString());
    }

    [Test]
    public async Task Should_rethrow_when_disabling_error_queue()
    {
        // the prefix will be configured using the transport's prefix configuration therefore we remove it for the endpoint name
        var endpointName = QueueName;

        var receivedMessages = await GenerateAndReceiveSQSEvent<TestMessage>();

        var context = new TestContext();

        var endpoint = new AwsLambdaSQSEndpoint(ctx =>
        {
            var configuration = new AwsLambdaSQSEndpointConfiguration(endpointName, CreateSQSClient(), CreateSNSClient());

            configuration.DoNotSendMessagesToErrorQueue();

            var advanced = configuration.AdvancedConfiguration;
            advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
            advanced.Recoverability().Immediate(i => i.NumberOfRetries(0));
            advanced.SendFailedMessagesTo(ErrorQueueName);

            return configuration;
        });

        var exception = Assert.ThrowsAsync<Exception>(() => endpoint.Process(receivedMessages, null));

        StringAssert.Contains("Failed to process message", exception.Message);
        Assert.AreEqual("simulated exception", exception.InnerException.Message);
        Assert.AreEqual(0, await CountMessagesInErrorQueue());
    }

    public class TestContext
    {
    }

    public class TestMessage : ICommand
    {
    }

    public class FailingMessageHandler : IHandleMessages<TestMessage>
    {
        public Task Handle(TestMessage message, IMessageHandlerContext context) => throw new Exception("simulated exception");
    }
}