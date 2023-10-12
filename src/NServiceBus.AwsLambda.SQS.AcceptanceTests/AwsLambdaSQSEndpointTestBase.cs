﻿namespace NServiceBus.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
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
        protected const string DelayedDeliveryQueueSuffix = "-delay.fifo";
        const int QueueDelayInSeconds = 900; // 15 * 60

        protected string QueueName { get; private set; }

        protected string DelayQueueName { get; private set; }

        protected string ErrorQueueName { get; private set; }

        protected string QueueNamePrefix { get; set; }

        protected string BucketName { get; } = Environment.GetEnvironmentVariable("NSERVICEBUS_AMAZONSQS_S3BUCKET");
        protected string KeyPrefix { get; private set; }


        [SetUp]
        public virtual async Task Setup()
        {
            queueNames = new List<string>();

            QueueNamePrefix ??= Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();

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
            DelayQueueName = $"{QueueName}{DelayedDeliveryQueueSuffix}";
            _ = await sqsClient.CreateQueueAsync(new CreateQueueRequest(DelayQueueName)
            {
                Attributes = new Dictionary<string, string>
                {
                    { "FifoQueue", "true" },
                    { QueueAttributeName.DelaySeconds, QueueDelayInSeconds.ToString(CultureInfo.InvariantCulture)}
                }
            });
            RegisterQueueNameToCleanup(DelayQueueName);

            s3Client = CreateS3Client();
            KeyPrefix = QueueNamePrefix;
        }

        [TearDown]
        public async Task TearDown()
        {
            // clear queue name prefix after each test, otherwise classes with multiple tests in the same class will run into issues
            QueueNamePrefix = null;

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

        protected async Task<SQSEvent> GenerateAndReceiveSQSEvent<T>(int count = 1) where T : new()
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
                AttributeNames = new List<string> { "SentTimestamp" },
                MessageAttributeNames = new List<string> { "*" }
            };

            var receivedMessages = await sqsClient.ReceiveMessageAsync(receiveRequest);

            return receivedMessages.ToSQSEvent();
        }

        protected async Task<SQSEvent> GenerateAndReceiveNativeSQSEvent(Dictionary<string, MessageAttributeValue> messageAttributeValues, string message, bool base64Encode = true)
        {
            var body = base64Encode ? Convert.ToBase64String(Encoding.UTF8.GetBytes(message)) : message;

            var sendMessageRequest = new SendMessageRequest
            {
                QueueUrl = createdQueue.QueueUrl,
                MessageAttributes = messageAttributeValues,
                MessageBody = body
            };

            await sqsClient.SendMessageAsync(sendMessageRequest)
                .ConfigureAwait(false);

            var receiveRequest = new ReceiveMessageRequest(createdQueue.QueueUrl)
            {
                MaxNumberOfMessages = 10,
                WaitTimeSeconds = 20,
                AttributeNames = new List<string> { "SentTimestamp" },
                MessageAttributeNames = new List<string> { "*" }
            };

            var receivedMessages = await sqsClient.ReceiveMessageAsync(receiveRequest);

            return receivedMessages.ToSQSEvent();
        }

        protected async Task UploadMessageBodyToS3(string key, string body) =>
            await s3Client.PutObjectAsync(new PutObjectRequest
            {
                Key = $"{key}",
                BucketName = BucketName,
                ContentBody = body
            });

        protected async Task<int> CountMessagesInErrorQueue()
        {
            var attReq = new GetQueueAttributesRequest { QueueUrl = createdErrorQueue.QueueUrl };
            attReq.AttributeNames.Add("ApproximateNumberOfMessages");
            var response = await sqsClient.GetQueueAttributesAsync(attReq).ConfigureAwait(false);
            return response.ApproximateNumberOfMessages;
        }

        protected async Task<SQSEvent> RetrieveMessagesInErrorQueue(int maxMessageCount = 10)
        {
            var receiveRequest = new ReceiveMessageRequest(createdErrorQueue.QueueUrl)
            {
                MaxNumberOfMessages = maxMessageCount,
                WaitTimeSeconds = 20,
                AttributeNames = new List<string> { "SentTimestamp" },
                MessageAttributeNames = new List<string> { "*" }
            };

            var receivedMessages = await sqsClient.ReceiveMessageAsync(receiveRequest);

            return receivedMessages.ToSQSEvent();
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