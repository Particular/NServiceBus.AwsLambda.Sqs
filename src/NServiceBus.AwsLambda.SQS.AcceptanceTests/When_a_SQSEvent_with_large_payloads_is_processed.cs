namespace NServiceBus.AwsLambda.Tests
{
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    class When_a_SQSEvent_with_large_payloads_is_processed : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_handlers_should_be_invoked_and_process_successfully()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<MessageWithLargePayload>(3);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());
                var transport = configuration.Transport;


                transport.S3 = new S3Settings(BucketName, KeyPrefix, CreateS3Client());


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