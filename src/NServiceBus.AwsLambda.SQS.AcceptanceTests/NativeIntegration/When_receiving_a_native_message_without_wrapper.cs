namespace NServiceBus.AcceptanceTests.NativeIntegration
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Amazon.SQS.Model;
    using Microsoft.Extensions.DependencyInjection;
    using NServiceBus.AcceptanceTests;
    using NUnit.Framework;

    class When_receiving_a_native_message_without_wrapper : AwsLambdaSQSEndpointTestBase
    {
        static readonly string MessageToSend = new XDocument(new XElement("NServiceBus.AcceptanceTests.NativeIntegration.NativeMessage", new XElement("ThisIsTheMessage", "Hello!"))).ToString();

        [Test]
        public async Task Should_be_processed_when_nsbheaders_present_with_messageid()
        {
            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {
                    "NServiceBus.AmazonSQS.Headers",
                    new MessageAttributeValue
                    {
                        DataType = "String", StringValue = GetHeaders(messageId: Guid.NewGuid().ToString())
                    }
                }
            }, MessageToSend, base64Encode: false);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());
                var transport = configuration.Transport;

                transport.S3 = new S3Settings(BucketName, KeyPrefix, CreateS3Client());

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.AreEqual("Hello!", context.MessageReceived);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount);
        }

        [Test]
        public async Task Should_be_processed_when_nsbheaders_present_without_messageid()
        {
            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {
                    "NServiceBus.AmazonSQS.Headers",
                    new MessageAttributeValue
                    {
                        DataType = "String", StringValue = GetHeaders()
                    }
                }
            }, MessageToSend, base64Encode: false);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());
                var transport = configuration.Transport;

                transport.S3 = new S3Settings(BucketName, KeyPrefix, CreateS3Client());

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.AreEqual("Hello!", context.MessageReceived);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount);
        }

        [Test]
        public async Task Should_support_loading_body_from_s3()
        {
            var s3Key = Guid.NewGuid().ToString();

            await UploadMessageBodyToS3(s3Key, MessageToSend);

            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {
                    "NServiceBus.AmazonSQS.Headers",
                    new MessageAttributeValue
                    {
                        DataType = "String", StringValue = GetHeaders(s3Key: s3Key)
                    }
                }
            }, MessageToSend, base64Encode: false);

            var context = new TestContext();

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());
                var transport = configuration.Transport;

                transport.S3 = new S3Settings(BucketName, KeyPrefix, CreateS3Client());

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.RegisterComponents(c => c.AddSingleton(typeof(TestContext), context));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            Assert.AreEqual("Hello!", context.MessageReceived);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount);
        }

        string GetHeaders(string s3Key = null, string messageId = null)
        {
            var nsbHeaders = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(s3Key))
            {
                nsbHeaders.Add("S3BodyKey", "s3Key");
            }

            if (!string.IsNullOrEmpty(messageId))
            {
                nsbHeaders.Add("NServiceBus.MessageId", messageId);
            }

            return JsonSerializer.Serialize(nsbHeaders);
        }

        public class TestContext
        {
            public string MessageReceived { get; set; }
        }

        public class NativeHandler : IHandleMessages<NativeMessage>
        {
            public NativeHandler(TestContext context) => testContext = context;

            public Task Handle(NativeMessage message, IMessageHandlerContext context)
            {
                testContext.MessageReceived = message.ThisIsTheMessage;
                return Task.CompletedTask;
            }

            readonly TestContext testContext;
        }
    }

    public class NativeMessage : IMessage
    {
        public string ThisIsTheMessage { get; set; }
    }
}