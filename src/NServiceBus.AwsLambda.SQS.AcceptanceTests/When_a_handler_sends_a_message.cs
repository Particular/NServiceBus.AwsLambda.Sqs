namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;

    class When_a_handler_sends_a_message : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task The_message_should_be_received()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<MessageThatTriggersASentMessage>(1);

            var context = new TestContext();

            var destinationEndpointName = "DestinationEndpoint";
            RegisterQueueNameToCleanup($"{Prefix}{destinationEndpointName}");
            RegisterQueueNameToCleanup($"{Prefix}{destinationEndpointName}{DelayedDeliveryQueueSuffix}");

            var destinationConfiguration = new EndpointConfiguration(destinationEndpointName);

            destinationConfiguration.UseSerialization<SystemJsonSerializer>();
            destinationConfiguration.EnableInstallers();
            destinationConfiguration.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
            destinationConfiguration.UseTransport(new SqsTransport(CreateSQSClient(), CreateSNSClient())
            {
                QueueNamePrefix = Prefix
            });

            var destinationEndpoint = await Endpoint.Start(destinationConfiguration);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = DefaultLambdaEndpointConfiguration(context);
                configuration.RoutingSettings.RouteToEndpoint(typeof(SentMessage), destinationEndpointName);
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            await context.MessageReceived.Task.WaitAsync(TimeSpan.FromMinutes(1));

            await destinationEndpoint.Stop();

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.That(messagesInErrorQueueCount, Is.EqualTo(0), "Error queue count mismatch");
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