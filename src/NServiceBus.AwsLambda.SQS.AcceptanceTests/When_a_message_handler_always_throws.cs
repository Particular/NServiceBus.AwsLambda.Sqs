namespace NServiceBus.AwsLambda.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;

    class When_a_message_handler_always_throws : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_messages_should_forward_to_error_queue_by_default()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<AlwaysFailsMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new SQSTriggeredEndpointConfiguration(QueueName);
                var transport = configuration.Transport;
                transport.ClientFactory(CreateSQSClient);

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(receivedMessages.Records.Count, messagesInErrorQueueCount, "Error queue count mismatch");

            Assert.AreEqual(messagesInErrorQueueCount * 6, context.HandlerInvokationCount, "Immediate/Delayed Retry count mismatch");
        }

        public class TestContext
        {
            public int HandlerInvokationCount
            {
                get { return count; }
            }

            public void HandlerInvoked() => Interlocked.Increment(ref count);
            int count;
        }

        public class AlwaysFailsMessage : ICommand
        {
        }

        public class AlwaysFailsMessageHandler : IHandleMessages<AlwaysFailsMessage>
        {
            public AlwaysFailsMessageHandler(TestContext context)
            {
                testContext = context;
            }

            public Task Handle(AlwaysFailsMessage message, IMessageHandlerContext context)
            {
                testContext.HandlerInvoked();
                throw new Exception("Simulated exception");
            }

            TestContext testContext;
        }
    }
}