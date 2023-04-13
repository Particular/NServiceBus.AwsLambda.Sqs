namespace NServiceBus.AcceptanceTests
{
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    using Message = Amazon.SQS.Model.Message;

    class When_a_SQSEvent_is_processed : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_handlers_should_be_invoked_and_process_successfully()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<SuccessMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.AreEqual(receivedMessages.Records.Count, context.HandlerInvokationCount);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount);
        }

        [Test]
        public async Task The_native_message_should_be_available_on_the_context()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<SuccessMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.IsNotNull(context.NativeMessage);
            Assert.AreEqual(receivedMessages.Records[0].MessageId, context.NativeMessage.MessageId);
        }

        public class TestContext
        {
            public int HandlerInvokationCount => count;

            public void HandlerInvoked() => Interlocked.Increment(ref count);
            int count;

            public Message NativeMessage { get; set; }
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

                testContext.NativeMessage = nativeMessage;

                testContext.HandlerInvoked();
                return Task.CompletedTask;
            }

            TestContext testContext;
        }
    }
}