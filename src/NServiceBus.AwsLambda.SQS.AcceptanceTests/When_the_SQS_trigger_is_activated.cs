using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NServiceBus.AwsLambda.Tests
{
    class When_a_SQSEvent_is_processed : MockLambdaTest
    {
        [Test]
        public async Task The_handlers_should_be_invoked_and_process_successfully()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<SuccessMessage>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new SQSTriggeredEndpointConfiguration(QueueName);
                configuration.AdvancedConfiguration.SendFailedMessagesTo(ErrorQueueName);
                configuration.AdvancedConfiguration.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.AreEqual(receivedMessages.Records.Count, context.HandlerInvokationCount);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount);
        }

        public class TestContext
        {
            int count;
            public int HandlerInvokationCount { get { return count; } }
            public void HandlerInvoked() => Interlocked.Increment(ref count);
        }

        public class SuccessMessage : ICommand
        {
        }

        public class SuccessMessageHandler : IHandleMessages<SuccessMessage>
        {
            TestContext testContext;
            public SuccessMessageHandler(TestContext context)
            {
                testContext = context;
            }

            public Task Handle(SuccessMessage message, IMessageHandlerContext context)
            {
                testContext.HandlerInvoked();
                return Task.CompletedTask;
            }
        }
    }
}
