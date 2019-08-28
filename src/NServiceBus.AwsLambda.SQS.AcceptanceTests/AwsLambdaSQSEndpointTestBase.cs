namespace NServiceBus.AwsLambda.Tests
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Amazon.Lambda.SQSEvents;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using NUnit.Framework;
    using SQS.AcceptanceTests;

    [TestFixture]
    class AwsLambdaSQSEndpointTestBase
    {
        protected string QueueName { get; set; }
        protected string ErrorQueueName { get; set; }

        protected string QueueNamePrefix { get; set; }

        protected string BucketName { get; set; }
        protected string KeyPrefix { get; set; }

        [SetUp]
        public async Task Setup()
        {
            queueNames = new List<string>();

            QueueNamePrefix = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();

            QueueName = $"{QueueNamePrefix}testqueue";
            sqsClient = CreateSQSClient();
            createdQueue = await sqsClient.CreateQueueAsync(new CreateQueueRequest(QueueName)
            {
                Attributes = new Dictionary<string, string>
                {
                    {QueueAttributeName.VisibilityTimeout, "10"}
                }
            });
            RegisterQueueNameToCleanup(QueueName);
            ErrorQueueName = $"{QueueNamePrefix}error";
            createdErrorQueue = await sqsClient.CreateQueueAsync(new CreateQueueRequest(ErrorQueueName)
            {
                Attributes = new Dictionary<string, string>
                {
                    {QueueAttributeName.VisibilityTimeout, "10"}
                }
            });
            RegisterQueueNameToCleanup(ErrorQueueName);
            s3Client = CreateS3Client();
            BucketName = $"{QueueNamePrefix}bucket";
            KeyPrefix = QueueNamePrefix;
            await s3Client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = BucketName
            });
        }

        [TearDown]
        public async Task TearDown()
        {
            var queueUrls = queueNames.Select(name => sqsClient.GetQueueUrlAsync(name));
            await Task.WhenAll(queueUrls);
            var queueDeletions = queueUrls.Select(x => x.Result.QueueUrl).Select(url => sqsClient.DeleteQueueAsync(url));
            await Task.WhenAll(queueDeletions);
            var objects = await s3Client.ListObjectsAsync(BucketName);

            if (objects.S3Objects.Any())
            {
                await s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = BucketName,
                    Objects = new List<KeyVersion>(objects.S3Objects.Select(o => new KeyVersion
                    {
                        Key = o.Key
                    }))
                });
            }

            await s3Client.DeleteBucketAsync(new DeleteBucketRequest
            {
                BucketName = BucketName
            });
        }

        protected void RegisterQueueNameToCleanup(string queueName)
        {
            queueNames.Add(queueName);
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
                MaxNumberOfMessages = count
            };

            var receivedMessages = await sqsClient.ReceiveMessageAsync(receiveRequest);

            return receivedMessages.ToSQSEvent();
        }

        protected async Task<int> CountMessagesInErrorQueue()
        {
            var messagesInErrorQueueResponse = await sqsClient.ReceiveMessageAsync(new ReceiveMessageRequest(createdErrorQueue.QueueUrl)
            {
                MaxNumberOfMessages = 10
            });

            return messagesInErrorQueueResponse.Messages.Count;
        }

        public static IAmazonSQS CreateSQSClient()
        {
            var credentials = new EnvironmentVariablesAWSCredentials();
            return new AmazonSQSClient(credentials);
        }

        public static IAmazonS3 CreateS3Client()
        {
            var credentials = new EnvironmentVariablesAWSCredentials();
            return new AmazonS3Client(credentials);
        }

        private List<string> queueNames;

        private IAmazonSQS sqsClient;
        private CreateQueueResponse createdQueue;
        private CreateQueueResponse createdErrorQueue;
        private IAmazonS3 s3Client;
    }
}