namespace NServiceBus.AcceptanceTests
{
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using System.IO;
    using System.Threading.Tasks;

    class When_configuring_queue_name_prefix : AwsLambdaSQSEndpointTestBase
    {
        public string Prefix { get; } = "test-";

        public override Task Setup()
        {
            QueueNamePrefix = Prefix + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();
            return base.Setup();
        }

        [Test]
        public async Task The_message_should_be_received_from_prefixed_queue()
        {
            // the prefix will be configured using the transport's prefix configuration therefore we remove it for the endpoint name
            var endpointName = QueueName.Substring(Prefix.Length);

            var receivedMessages = await GenerateAndReceiveSQSEvent<SentMessage>();

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(endpointName, CreateSQSClient(), CreateSNSClient());

                configuration.Transport.QueueNamePrefix = Prefix;

                var advanced = configuration.AdvancedConfiguration;
                advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));

                // SQS will add the specified queue prefix to the configured error queue
                advanced.SendFailedMessagesTo(ErrorQueueName.Substring(Prefix.Length));

                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.IsTrue(context.MessageHandled);
        }

        public class TestContext
        {
            public bool MessageHandled { get; set; }
        }

        public class SentMessage : ICommand
        {
        }

        public class MessageHandlerThatReceivesSentMessage : IHandleMessages<SentMessage>
        {
            public MessageHandlerThatReceivesSentMessage(TestContext context) => testContext = context;

            public Task Handle(SentMessage message, IMessageHandlerContext context)
            {
                testContext.MessageHandled = true;
                return Task.CompletedTask;
            }

            readonly TestContext testContext;
        }
    }
}