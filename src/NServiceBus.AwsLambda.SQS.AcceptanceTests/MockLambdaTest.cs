namespace NServiceBus.AwsLambda.Tests
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Amazon.Lambda.SQSEvents;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using NUnit.Framework;
    using SQS.AcceptanceTests;

    [TestFixture]
    class MockLambdaTest
    {
        protected string QueueName { get; set; }
        protected string ErrorQueueName { get; set; }

        protected string QueueNamePrefix { get; set; }
        
        protected string BucketName { get; set; }
        protected string KeyPrefix { get; set; }

        [SetUp]
        public async Task Setup()
        {
            QueueNamePrefix = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();

            QueueName = $"{QueueNamePrefix}testqueue";
            sqsClient = new AmazonSQSClient();
            createdQueue = await sqsClient.CreateQueueAsync(new CreateQueueRequest(QueueName)
            {
                Attributes = new Dictionary<string, string>
                {
                    { QueueAttributeName.VisibilityTimeout, "10" }
                }
            });
            ErrorQueueName = $"{QueueNamePrefix}error";
            createdErrorQueue = await sqsClient.CreateQueueAsync(new CreateQueueRequest(ErrorQueueName)
            {
                Attributes = new Dictionary<string, string>
                {
                    { QueueAttributeName.VisibilityTimeout, "10" }
                }
            });
            s3Client = new AmazonS3Client();
            BucketName = $"{QueueNamePrefix}bucket";
            KeyPrefix = QueueNamePrefix;
            await s3Client.PutBucketAsync(new PutBucketRequest()
            {
                BucketName = BucketName,
            });
        }

        [TearDown]
        public async Task TearDown()
        {
            await sqsClient.DeleteQueueAsync(createdQueue.QueueUrl);
            await sqsClient.DeleteQueueAsync(createdErrorQueue.QueueUrl);
            await s3Client.DeleteBucketAsync(BucketName);
        }

        protected async Task<SQSEvent> GenerateAndReceiveSQSEvent<T>(int count) where T : new()
        {
            var endpointConfiguration = new EndpointConfiguration($"{QueueNamePrefix}sender");
            endpointConfiguration.SendOnly();
            endpointConfiguration.UsePersistence<InMemoryPersistence>();
            var transport = endpointConfiguration.UseTransport<SqsTransport>();
            transport.S3(BucketName, KeyPrefix);

            var endpointInstance = await Endpoint.Start(endpointConfiguration)
                .ConfigureAwait(false);

            for (var i = 0; i < count; i++)
            {
                await endpointInstance.Send(QueueName, new T());
            }

            await endpointInstance.Stop();

            await Task.Delay(30);

            var receiveRequest = new ReceiveMessageRequest(createdQueue.QueueUrl)
            {
                MaxNumberOfMessages = count,
            };

            var receivedMessages = await sqsClient.ReceiveMessageAsync(receiveRequest);

            return receivedMessages.ToSQSEvent();
        }

        protected async Task<int> CountMessagesInErrorQueue()
        {
            var messagesInErrorQueueResponse = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest(createdErrorQueue.QueueUrl)
            {
                MaxNumberOfMessages = 10,
            });

            return messagesInErrorQueueResponse.Messages.Count;
        }

        protected Task<int> CountMessagesInInputQueue()
        {
            return Task.FromResult(0);
        }

        private AmazonSQSClient sqsClient;
        private CreateQueueResponse createdQueue;
        private CreateQueueResponse createdErrorQueue;
        private AmazonS3Client s3Client;
    }
}