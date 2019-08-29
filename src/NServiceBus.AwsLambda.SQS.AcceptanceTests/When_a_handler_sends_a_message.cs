namespace NServiceBus.AwsLambda.Tests
{
    using System.Threading.Tasks;
    using NUnit.Framework;

    class When_a_handler_sends_a_message : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_message_should_be_received()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<MessageThatTriggersASentMessage>(1);
            
            var context = new TestContext();

            var destinationEndpointName = $"{QueueNamePrefix}DestinationEndpoint";
            RegisterQueueNameToCleanup(destinationEndpointName);

            var destinationConfiguration = new EndpointConfiguration(destinationEndpointName);
            destinationConfiguration.UsePersistence<InMemoryPersistence>();
            var destinationTransport = destinationConfiguration.UseTransport<SqsTransport>();
            destinationTransport.ClientFactory(CreateSQSClient);
            destinationConfiguration.SendFailedMessagesTo(ErrorQueueName);
            destinationConfiguration.EnableInstallers();
            destinationConfiguration.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
            var destinationEndpoint = await Endpoint.Start(destinationConfiguration);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new SQSTriggeredEndpointConfiguration(QueueName);
                var transport = configuration.Transport;
                transport.ClientFactory(CreateSQSClient);

                var routing = transport.Routing();
                routing.RouteToEndpoint(typeof(SentMessage), destinationEndpointName);

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            await context.MessageReceived.Task;

            await destinationEndpoint.Stop();

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount, "Error queue count mismatch");
        }

        public class TestContext
        {
            public TaskCompletionSource<bool> MessageReceived { get; set; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public class MessageThatTriggersASentMessage : ICommand
        {
        }

        public class SentMessage : ICommand
        {
        }

        public class MessageHandlerThatSends : IHandleMessages<MessageThatTriggersASentMessage>
        {
            public Task Handle(MessageThatTriggersASentMessage message, IMessageHandlerContext context)
            {
                return context.Send(new SentMessage());
            }
        }

        public class MessageHandlerThatReceivesSentMessage : IHandleMessages<SentMessage>
        {
            public MessageHandlerThatReceivesSentMessage(TestContext context)
            {
                testContext = context;
            }

            public Task Handle(SentMessage message, IMessageHandlerContext context)
            {
                testContext.MessageReceived.TrySetResult(true);
                return Task.CompletedTask;
            }

            TestContext testContext;
        }
    }
}