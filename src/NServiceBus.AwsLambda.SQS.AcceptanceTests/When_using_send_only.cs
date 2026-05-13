namespace NServiceBus.AcceptanceTests
{
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using NServiceBus;
    using NUnit.Framework;

    class When_using_sendonly : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task Should_throw_on_process()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<SentMessage>(1);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = DefaultLambdaEndpointConfiguration();
                configuration.AdvancedConfiguration.SendOnly();
                return configuration;
            });

            Assert.ThrowsAsync<System.InvalidOperationException>(() => endpoint.Process(receivedMessages, null));
        }

        [Test]
        public async Task Should_send_messages()
        {
            var context = new TestContext();

            var destinationEndpointName = $"{Prefix}DestinationEndpoint";
            RegisterQueueNameToCleanup(destinationEndpointName);
            RegisterQueueNameToCleanup(destinationEndpointName + DelayedDeliveryQueueSuffix);

            var destinationConfiguration = new EndpointConfiguration(destinationEndpointName);

            var destinationTransport = new SqsTransport(CreateSQSClient(), CreateSNSClient());

            destinationConfiguration.UseSerialization<SystemJsonSerializer>();
            destinationConfiguration.EnableInstallers();
            destinationConfiguration.UseTransport(destinationTransport);

            var builder = Host.CreateApplicationBuilder();
            builder.Services.AddSingleton(typeof(TestContext), context);
            builder.Services.AddNServiceBusEndpoint(destinationConfiguration);

            var destinationHost = builder.Build();
            await destinationHost.StartAsync();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = DefaultLambdaEndpointConfiguration(context);
                configuration.RoutingSettings.RouteToEndpoint(typeof(SentMessage), destinationEndpointName);
                configuration.AdvancedConfiguration.SendOnly();
                return configuration;
            });

            await endpoint.Send(new SentMessage(), null);

            await context.MessageReceived.Task;

            await destinationHost.StopAsync();

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.That(messagesInErrorQueueCount, Is.EqualTo(0), "Error queue count mismatch");
        }

        public class TestContext
        {
            public TaskCompletionSource<bool> MessageReceived { get; set; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public class SentMessage : ICommand
        {
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
