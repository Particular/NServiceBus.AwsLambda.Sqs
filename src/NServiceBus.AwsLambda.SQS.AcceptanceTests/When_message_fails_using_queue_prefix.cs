namespace NServiceBus.AcceptanceTests;

using System;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

class When_message_fails_using_queue_prefix : AwsLambdaSQSEndpointTestBase
{
    public string Prefix { get; } = "test-";

    public override Task Setup()
    {
        QueueNamePrefix = Prefix + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();
        return base.Setup();
    }

    [Test]
    public async Task Should_move_message_to_prefixed_error_queue()
    {
        // the prefix will be configured using the transport's prefix configuration therefore we remove it for the endpoint name
        var endpointName = QueueName.Substring(Prefix.Length);

        var receivedMessages = await GenerateAndReceiveSQSEvent<TestMessage>();

        var context = new TestContext();

        var endpoint = new AwsLambdaSQSEndpoint(ctx =>
        {
            var configuration = new AwsLambdaSQSEndpointConfiguration(endpointName, CreateSQSClient(), CreateSNSClient());
            configuration.Transport.QueueNamePrefix = Prefix;

            var advanced = configuration.AdvancedConfiguration;
            advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
            advanced.Recoverability().Immediate(i => i.NumberOfRetries(0));

            advanced.SendFailedMessagesTo(ErrorQueueName.Substring(Prefix.Length));

            return configuration;
        });

        Assert.DoesNotThrowAsync(() => endpoint.Process(receivedMessages, null), "message should be moved to the error queue instead");

        var errorMessages = await RetrieveMessagesInErrorQueue();
        Assert.AreEqual(1, errorMessages.Records.Count);
        JsonDocument errorMessage = JsonSerializer.Deserialize<JsonDocument>(errorMessages.Records.First().Body);
        var errorMessageHeader = errorMessage.RootElement.GetProperty("Headers");
        Assert.AreEqual("simulated exception", errorMessageHeader.GetProperty("NServiceBus.ExceptionInfo.Message").GetString());
        Assert.AreEqual(endpointName, errorMessageHeader.GetProperty("NServiceBus.ProcessingEndpoint").GetString());
        StringAssert.EndsWith(QueueName, errorMessageHeader.GetProperty("NServiceBus.FailedQ").GetString());
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