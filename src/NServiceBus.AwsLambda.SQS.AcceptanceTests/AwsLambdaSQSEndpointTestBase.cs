namespace NServiceBus.AwsLambda.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Amazon.Lambda.SQSEvents;
    using Amazon.Runtime;
    using Amazon.S3;
    using Amazon.S3.Model;
    using Amazon.SimpleNotificationService;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using NUnit.Framework;

    [TestFixture]
    class AwsLambdaSQSEndpointTestBase
    {
        protected string QueueName { get; set; }
        protected string ErrorQueueName { get; set; }

        protected string QueueNamePrefix { get; set; }

        protected string BucketName { get; } = Environment.GetEnvironmentVariable("NSERVICEBUS_AMAZONSQS_S3BUCKET");
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
            KeyPrefix = QueueNamePrefix;
        }

        [TearDown]
        public async Task TearDown()
        {
            var queueUrls = queueNames.Select(name => sqsClient.GetQueueUrlAsync(name));
            await Task.WhenAll(queueUrls);
            var queueDeletions = queueUrls.Select(x => x.Result.QueueUrl).Select(url => sqsClient.DeleteQueueAsync(url));
            await Task.WhenAll(queueDeletions);
            var objects = await s3Client.ListObjectsAsync(new ListObjectsRequest
            {
                BucketName = BucketName,
                Prefix = KeyPrefix
            });

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
        }

        protected void RegisterQueueNameToCleanup(string queueName)
        {
            queueNames.Add(queueName);
        }

        protected async Task<SQSEvent> GenerateAndReceiveSQSEvent<T>(int count) where T : new()
        {
            var endpointConfiguration = new EndpointConfiguration($"{QueueNamePrefix}sender");
            endpointConfiguration.SendOnly();

            var transport = new SqsTransport(CreateSQSClient(), CreateSNSClient())
            {
                S3 = new S3Settings(BucketName, KeyPrefix, CreateS3Client())
            };

            endpointConfiguration.UseTransport(transport);

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
                WaitTimeSeconds = 20,
            };

            var receivedMessages = await sqsClient.ReceiveMessageAsync(receiveRequest);

            return receivedMessages.ToSQSEvent();
        }

        protected async Task<int> CountMessagesInErrorQueue()
        {
            var attReq = new GetQueueAttributesRequest { QueueUrl = createdErrorQueue.QueueUrl };
            attReq.AttributeNames.Add("ApproximateNumberOfMessages");
            var response = await sqsClient.GetQueueAttributesAsync(attReq).ConfigureAwait(false);
            return response.ApproximateNumberOfMessages;
        }

        public static IAmazonSQS CreateSQSClient()
        {
            var credentials = new EnvironmentVariablesAWSCredentials();
            return new AmazonSQSClient(credentials);
        }

        public static IAmazonSimpleNotificationService CreateSNSClient()
        {
            var credentials = new EnvironmentVariablesAWSCredentials();
            return new AmazonSimpleNotificationServiceClient(credentials);
        }

        public static IAmazonS3 CreateS3Client()
        {
            var credentials = new EnvironmentVariablesAWSCredentials();
            return new AmazonS3Client(credentials);
        }

        List<string> queueNames;

        IAmazonSQS sqsClient;
        CreateQueueResponse createdQueue;
        CreateQueueResponse createdErrorQueue;
        IAmazonS3 s3Client;
    }
}