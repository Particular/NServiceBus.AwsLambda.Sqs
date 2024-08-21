namespace NServiceBus.AcceptanceTests
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.SQSEvents;
    using NUnit.Framework;

    using Message = Amazon.SQS.Model.Message;

    class When_a_SQSEvent_is_processed : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_handlers_should_be_invoked_and_process_successfully()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<SuccessMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context));

            await endpoint.Process(receivedMessages, null);

            Assert.That(context.HandlerInvokationCount, Is.EqualTo(receivedMessages.Records.Count));

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.That(messagesInErrorQueueCount, Is.EqualTo(0));
        }

        [Test]
        public async Task The_native_message_should_be_available_on_the_context()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<SuccessMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context));

            await endpoint.Process(receivedMessages, null);

            Assert.That(context.NativeMessage, Is.Not.Null, "SQS native message not found");
            Assert.That(context.LambdaNativeMessage, Is.Not.Null, "Lambda native message not found");
            Assert.That(receivedMessages.Records.Any(r => r.MessageId == context.NativeMessage.MessageId));
            Assert.That(receivedMessages.Records.Any(r => r.MessageId == context.LambdaNativeMessage.MessageId));
        }

        public class TestContext
        {
            public int HandlerInvokationCount => count;

            public void HandlerInvoked() => Interlocked.Increment(ref count);
            int count;

            public Message NativeMessage { get; set; }

            public SQSEvent.SQSMessage LambdaNativeMessage { get; set; }
        }

        public class SuccessMessage : ICommand
        {
        }

        public class SuccessMessageHandler : IHandleMessages<SuccessMessage>
        {
            public SuccessMessageHandler(TestContext context)
            {
                testContext = context;
            }

            public Task Handle(SuccessMessage message, IMessageHandlerContext context)
            {
                var nativeMessage = context.Extensions.Get<Message>();
                var lambdaNativeMessage = context.Extensions.Get<SQSEvent.SQSMessage>();

                testContext.NativeMessage = nativeMessage;
                testContext.LambdaNativeMessage = lambdaNativeMessage;

                testContext.HandlerInvoked();
                return Task.CompletedTask;
            }

            TestContext testContext;
        }
    }
}