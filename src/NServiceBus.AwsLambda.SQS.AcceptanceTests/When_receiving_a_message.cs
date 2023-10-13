namespace NServiceBus.AcceptanceTests
{
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Amazon.SQS;
    using AwsLambda.SQS.TransportWrapper;
    using NUnit.Framework;
    using Transport;

    class When_receiving_a_message : AwsLambdaSQSEndpointTestBase
    {
        [Test]
        public async Task ReceiveAddress_should_match_the_queue_from_which_message_is_received()
        {
            var receivedMessages = await GenerateAndReceiveSQSEvent<MyMessage>(1);

            var messageProcessor = new FakeMessageProcessor();

            var sqsClient = CreateSQSClient();

            var receiveQueueUrl = (await sqsClient.GetQueueUrlAsync(QueueName)).QueueUrl;
            var errorQueueUrl = (await sqsClient.GetQueueUrlAsync(ErrorQueueName)).QueueUrl;

            var processor =
                new AwsLambdaSQSEndpointMessageProcessor(messageProcessor, sqsClient, new S3Settings(BucketName, KeyPrefix, CreateS3Client()), QueueName, receiveQueueUrl, errorQueueUrl);

            await processor.ProcessMessage(receivedMessages.Records[0], CancellationToken.None);

            Assert.AreEqual(QueueName, messageProcessor.ReceivedMessageContext.ReceiveAddress);
        }

        public class FakeMessageProcessor : IMessageProcessor
        {
            public string ReceiveAddress { get; set; }
            public MessageContext ReceivedMessageContext { get; set; }

            public Task<ErrorHandleResult> PushFailedMessage(ErrorContext errorContext)
            {
                throw new System.NotImplementedException();
            }

            public Task PushMessage(MessageContext messageContext)
            {
                ReceivedMessageContext = messageContext;
                return Task.CompletedTask;
            }
        }

        //Needed to register MyMessage in the MessageMetadataRegistry
        public class MyMessageHandler : IHandleMessages<MyMessage>
        {
            public Task Handle(MyMessage message, IMessageHandlerContext context) => throw new System.NotImplementedException();
        }

        public class MyMessage : ICommand
        {
        }
    }
}