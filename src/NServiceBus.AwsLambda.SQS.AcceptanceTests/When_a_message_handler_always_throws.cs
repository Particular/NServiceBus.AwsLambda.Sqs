using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NServiceBus.AwsLambda.Tests
{
    class When_a_message_handler_always_throws : MockLambdaTest
    {
        [Test]
        public async Task The_messages_should_forward_to_error_queue_by_default()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<AlwaysFailsMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new SQSTriggeredEndpointConfiguration(QueueName);
                configuration.AdvancedConfiguration.Recoverability().Delayed(settings => settings.NumberOfRetries(0));
                configuration.AdvancedConfiguration.SendFailedMessagesTo(ErrorQueueName);
                configuration.AdvancedConfiguration.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(receivedMessages.Records.Count, messagesInErrorQueueCount, "Error queue count mismatch");

            Assert.AreEqual(messagesInErrorQueueCount * 6, context.HandlerInvokationCount, "Immediate/Delayed Retry count mismatch");
        }

        public class TestContext
        {
            int count;
            public int HandlerInvokationCount { get { return count; } }
            public void HandlerInvoked() => Interlocked.Increment(ref count);
        }

        public class AlwaysFailsMessage : ICommand
        {
        }

        public class AlwaysFailsMessageHandler : IHandleMessages<AlwaysFailsMessage>
        {
            TestContext testContext;
            public AlwaysFailsMessageHandler(TestContext context)
            {
                testContext = context;
            }

            public Task Handle(AlwaysFailsMessage message, IMessageHandlerContext context)
            {
                testContext.HandlerInvoked();
                throw new Exception("Simulated exception");
            }
        }
    }
}
