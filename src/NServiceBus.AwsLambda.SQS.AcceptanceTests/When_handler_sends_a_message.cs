namespace NServiceBus.AwsLambda.Tests
{
    using System.Threading.Tasks;
    using NUnit.Framework;

    class When_handler_sends_a_message : MockLambdaTest
    {
        [Test]
        public async Task The_message_should_be_received()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<MessageThatTriggersASentMessage>(1);
            
            var context = new TestContext();
            
            var destinationConfiguration = new EndpointConfiguration($"{QueueNamePrefix}DestinationEndpoint");
            destinationConfiguration.UsePersistence<InMemoryPersistence>();
            destinationConfiguration.UseTransport<SqsTransport>();
            destinationConfiguration.SendFailedMessagesTo(ErrorQueueName);
            destinationConfiguration.EnableInstallers();
            destinationConfiguration.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
            var destinationEndpoint = await Endpoint.Start(destinationConfiguration);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new SQSTriggeredEndpointConfiguration(QueueName);
                var routing = configuration.Transport.Routing();
                routing.RouteToEndpoint(typeof(SentMessage), $"{QueueNamePrefix}DestinationEndpoint");
                configuration.AdvancedConfiguration.SendFailedMessagesTo(ErrorQueueName);
                configuration.AdvancedConfiguration.RegisterComponents(c => c.RegisterSingleton(typeof(TestContext), context));
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