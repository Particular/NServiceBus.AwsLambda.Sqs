namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    class When_message_fails_processing : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task Should_move_message_to_error_queue()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<TestMessage>();

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context));

            Assert.DoesNotThrowAsync(() => endpoint.Process(receivedMessages, null), "message should be moved to the error queue instead");

            var errorMessages = await RetrieveMessagesInErrorQueue();
            Assert.That(errorMessages.Records, Has.Count.EqualTo(1));
            JsonDocument errorMessage = JsonSerializer.Deserialize<JsonDocument>(errorMessages.Records.First().Body);
            var errorMessageHeader = errorMessage.RootElement.GetProperty("Headers");
            Assert.Multiple(() =>
            {
                Assert.That(errorMessageHeader.GetProperty("NServiceBus.ExceptionInfo.Message").GetString(), Is.EqualTo("simulated exception"));
                Assert.That(errorMessageHeader.GetProperty("NServiceBus.ProcessingEndpoint").GetString(), Is.EqualTo(QueueName));
                Assert.That(errorMessageHeader.GetProperty("NServiceBus.FailedQ").GetString(), Is.EqualTo(QueueAddress));
            });
        }

        [Test]
        public async Task Should_rethrow_when_disabling_error_queue()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<TestMessage>();

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = DefaultLambdaEndpointConfiguration(context);
                configuration.DoNotSendMessagesToErrorQueue();
                return configuration;
            });

            var exception = Assert.ThrowsAsync<Exception>(() => endpoint.Process(receivedMessages, null));

            Assert.That(exception.Message, Does.Contain("Failed to process message"));
            Assert.Multiple(async () =>
            {
                Assert.That(exception.InnerException.Message, Is.EqualTo("simulated exception"));
                Assert.That(await CountMessagesInErrorQueue(), Is.EqualTo(0));
            });
        }

        public class TestContext
        {
            public int HandlerInvocationCount;
        }

        public class TestMessage : ICommand
        {
        }

        public class FailingMessageHandler : IHandleMessages<TestMessage>
        {
            readonly TestContext testContext;

            public FailingMessageHandler(TestContext testContext) => this.testContext = testContext;

            public Task Handle(TestMessage message, IMessageHandlerContext context)
            {
                Interlocked.Increment(ref testContext.HandlerInvocationCount);
                throw new Exception("simulated exception");
            }
        }
    }
}