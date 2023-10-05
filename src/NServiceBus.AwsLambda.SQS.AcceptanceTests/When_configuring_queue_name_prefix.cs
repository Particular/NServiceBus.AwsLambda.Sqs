namespace NServiceBus.AcceptanceTests
{
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using System.IO;
    using System.Threading.Tasks;

    //TODO: verify that outgoing messages contain the correct replyto address/other queue related headers
    //TODO: verify outgoing messages are taking into account the prefix on routing - potentially something for SQS transport repo itself - do we also need to docs?
    //TODO: do we want a test where the non-prefixed queue exists (and we expect a different path of the code to cause a failure)
    //TODO: add similar test for error queue related behavior of the Lambda endpoint
    class When_configuring_queue_name_prefix : AwsLambdaSQSEndpointTestBase
    {
        public string Prefix { get; } = "test-";

        public override Task Setup()
        {
            // TODO: Should we rename QueueNamePrefix to something else to indicate that it doesn't use the transports setting and is only used to create unique queue names?
            QueueNamePrefix = Prefix + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();
            return base.Setup();
        }

        [Test]
        public async Task The_message_should_be_received()
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

                //TODO create separate test for error queue?
                //advanced.SendFailedMessagesTo(ErrorQueueName);

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