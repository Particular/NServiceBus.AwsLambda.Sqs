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
            Assert.AreEqual(1, errorMessages.Records.Count);
            JsonDocument errorMessage = JsonSerializer.Deserialize<JsonDocument>(errorMessages.Records.First().Body);
            var errorMessageHeader = errorMessage.RootElement.GetProperty("Headers");
            Assert.AreEqual("simulated exception", errorMessageHeader.GetProperty("NServiceBus.ExceptionInfo.Message").GetString());
            Assert.AreEqual(QueueName, errorMessageHeader.GetProperty("NServiceBus.ProcessingEndpoint").GetString());
            Assert.AreEqual(QueueAddress, errorMessageHeader.GetProperty("NServiceBus.FailedQ").GetString());
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

            StringAssert.Contains("Failed to process message", exception.Message);
            Assert.AreEqual("simulated exception", exception.InnerException.Message);
            Assert.AreEqual(0, await CountMessagesInErrorQueue());
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