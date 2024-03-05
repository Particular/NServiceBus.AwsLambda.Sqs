namespace NServiceBus.AcceptanceTests.NativeIntegration
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Amazon.SQS.Model;
    using NServiceBus.AcceptanceTests;
    using NUnit.Framework;

    class When_receiving_a_native_message_without_wrapper : AwsLambdaSQSEndpointTestBase
    {
        static readonly string MessageToSend = new XDocument(new XElement("NServiceBus.AcceptanceTests.NativeIntegration.NativeMessage", new XElement("ThisIsTheMessage", "Hello!"))).ToString();
        static readonly string FailingMessageToSend = new XDocument(new XElement("NServiceBus.AcceptanceTests.NativeIntegration.FailingNativeMessage", new XElement("ThisIsTheMessage", "Hello!"))).ToString();

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

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context, useXmlSerializer: true));

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

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context, useXmlSerializer: true));

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

            var endpoint = new AwsLambdaSQSEndpoint(_ => DefaultLambdaEndpointConfiguration(context, useXmlSerializer: true));

            await endpoint.Process(receivedMessages, null);

            Assert.AreEqual("Hello!", context.MessageReceived);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(0, messagesInErrorQueueCount);
        }

        [Test]
        public async Task Should_preserve_poison_message_attributes_in_error_queue()
        {
            var s3Key = Guid.NewGuid().ToString();

            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {
                    "NServiceBus.AmazonSQS.Headers",
                    new MessageAttributeValue
                    {
                        DataType = "String", StringValue = GetHeaders(messageId: Guid.NewGuid().ToString())
                    }
                },
                {"S3BodyKey", new MessageAttributeValue {DataType = "String", StringValue = s3Key}},
                {"CustomAttribute", new MessageAttributeValue {DataType="String", StringValue = "TestAttribute" } },
            }, "Bad XML", base64Encode: false);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = DefaultLambdaEndpointConfiguration(useXmlSerializer: true);
                var transport = configuration.Transport;

                transport.S3 = new S3Settings(BucketName, Prefix, CreateS3Client());

                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(1, messagesInErrorQueueCount);

            var errorMessages = await RetrieveMessagesInErrorQueue();

            var message = errorMessages?.Records[0];

            Assert.NotNull(message);
            Assert.That(message.MessageAttributes.ContainsKey("NServiceBus.AmazonSQS.Headers"), $"NServiceBus.AmazonSQS.Headers message attribute is missing.");
            Assert.That(message.MessageAttributes.ContainsKey("S3BodyKey"), "S3BodyKey message attribute is missing.");
            Assert.That(message.MessageAttributes.ContainsKey("CustomAttribute"), "CustomAttribute message attribute is missing.");
        }

        [Test]
        public async Task Should_preserve_message_attributes_in_error_queue()
        {
            var s3Key = Guid.NewGuid().ToString();

            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {
                    "NServiceBus.AmazonSQS.Headers",
                    new MessageAttributeValue
                    {
                        DataType = "String", StringValue = GetHeaders(messageId: Guid.NewGuid().ToString())
                    }
                },
                {"CustomAttribute", new MessageAttributeValue {DataType="String", StringValue = "TestAttribute" } },
            }, FailingMessageToSend, base64Encode: false);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = DefaultLambdaEndpointConfiguration(useXmlSerializer: true);
                var transport = configuration.Transport;

                transport.S3 = new S3Settings(BucketName, Prefix, CreateS3Client());

                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(1, messagesInErrorQueueCount);

            var errorMessages = await RetrieveMessagesInErrorQueue();

            var message = errorMessages?.Records[0];

            Assert.That(message.MessageAttributes.ContainsKey("CustomAttribute"), "CustomAttribute message attribute is missing.");
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

        public class FailingNativeHandler : IHandleMessages<FailingNativeMessage>
        {
            public Task Handle(FailingNativeMessage message, IMessageHandlerContext context)
            {
                throw new Exception();
            }
        }
    }

    public class NativeMessage : IMessage
    {
        public string ThisIsTheMessage { get; set; }
    }

    public class FailingNativeMessage : IMessage
    {
        public string ThisIsTheMessage { get; set; }
    }
}