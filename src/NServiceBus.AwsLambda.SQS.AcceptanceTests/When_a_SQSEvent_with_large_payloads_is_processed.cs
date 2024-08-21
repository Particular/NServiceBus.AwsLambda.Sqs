namespace NServiceBus.AcceptanceTests
{
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    class When_a_SQSEvent_with_large_payloads_is_processed : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_handlers_should_be_invoked_and_process_successfully()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<MessageWithLargePayload>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context));

            await endpoint.Process(receivedMessages, null);

            Assert.That(context.HandlerInvokationCount, Is.EqualTo(receivedMessages.Records.Count));

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.That(messagesInErrorQueueCount, Is.EqualTo(0));
        }

        public class TestContext
        {
            public int HandlerInvokationCount => count;

            public void HandlerInvoked() => Interlocked.Increment(ref count);
            int count;
        }

        public class MessageWithLargePayload : ICommand
        {
            public byte[] Payload { get; set; } = new byte[150 * 1024];
        }

        public class SuccessMessageHandler : IHandleMessages<MessageWithLargePayload>
        {
            public SuccessMessageHandler(TestContext context)
            {
                testContext = context;
            }

            public Task Handle(MessageWithLargePayload message, IMessageHandlerContext context)
            {
                testContext.HandlerInvoked();
                return Task.CompletedTask;
            }

            TestContext testContext;
        }
    }
}