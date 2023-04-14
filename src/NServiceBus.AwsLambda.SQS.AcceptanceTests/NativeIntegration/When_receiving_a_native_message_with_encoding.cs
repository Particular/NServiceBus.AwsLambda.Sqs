namespace NServiceBus.AcceptanceTests.NativeIntegration
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Amazon.SQS.Model;
    using Microsoft.Extensions.DependencyInjection;
    using NServiceBus.AcceptanceTests;
    using NUnit.Framework;

    class When_receiving_a_native_message_with_encoding : AwsLambdaSQSEndpointTestBase
    {
        static readonly string MessageToSend = new XDocument(new XElement("Message", new XElement("ThisIsTheMessage", "Hello!"))).ToString();
        static readonly string FailingMessageToSend = new XDocument(new XElement("FailingMessage", new XElement("ThisIsTheMessage", "Hello!"))).ToString();

        [Test]
        public async Task Should_be_processed_when_messagetypefullname_present()
        {
            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {"MessageTypeFullName", new MessageAttributeValue {DataType = "String", StringValue = typeof(Message).FullName}}
            }, MessageToSend);

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
        public async Task Should_fail_when_messagetypefullname_not_present()
        {
            var messageId = Guid.NewGuid();
            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {
                    Headers.MessageId, new MessageAttributeValue {DataType = "String", StringValue = messageId.ToString() }
                }
            }, MessageToSend);

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

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(1, messagesInErrorQueueCount);
        }

        [Test]
        public async Task Should_perserve_poison_message_attributes_in_error_queue()
        {
            var messageId = Guid.NewGuid();
            var s3Key = Guid.NewGuid().ToString();

            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                { Headers.MessageId, new MessageAttributeValue {DataType = "String", StringValue = messageId.ToString() }},
                {"S3BodyKey", new MessageAttributeValue {DataType = "String", StringValue = s3Key}},
                {"MessageTypeFullName", new MessageAttributeValue {DataType = "String", StringValue = typeof(Message).FullName}},
                {"CustomAttribute", new MessageAttributeValue {DataType="String", StringValue = "TestAttribute" } },

            }, "Invalid XML");

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

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(1, messagesInErrorQueueCount);

            var poisonMessages = await RetrieveMessagesInErrorQueue();
            var message = poisonMessages.Records[0];

            Assert.IsNotNull(message);
            Assert.That(message.MessageAttributes.ContainsKey(Headers.MessageId), "Message ID message attribute is missing.");
            Assert.That(message.MessageAttributes.ContainsKey("S3BodyKey"), "S3BodyKey message attribute is missing.");
            Assert.That(message.MessageAttributes.ContainsKey("MessageTypeFullName"), "MessageTypeFullName message attribute is missing.");
            Assert.That(message.MessageAttributes.ContainsKey("CustomAttribute"), "CustomAttribute message attribute is missing.");
        }

        [Test]
        public async Task Should_perserve_message_attributes_in_error_queue()
        {
            var messageId = Guid.NewGuid();
            var messageType = typeof(FailingNativeMessage).FullName;

            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                { Headers.MessageId, new MessageAttributeValue {DataType = "String", StringValue = messageId.ToString() }},
                {"MessageTypeFullName", new MessageAttributeValue {DataType = "String", StringValue = messageType }},
                {"CustomAttribute", new MessageAttributeValue {DataType="String", StringValue = "TestAttribute" } },

            }, FailingMessageToSend);

            var endpoint = new AwsLambdaSQSEndpoint(ctx =>
            {
                var configuration = new AwsLambdaSQSEndpointConfiguration(QueueName, CreateSQSClient(), CreateSNSClient());
                var transport = configuration.Transport;

                transport.S3 = new S3Settings(BucketName, KeyPrefix, CreateS3Client());

                var advanced = configuration.AdvancedConfiguration;
                advanced.SendFailedMessagesTo(ErrorQueueName);
                advanced.Recoverability().Immediate(s => s.NumberOfRetries(0));
                return configuration;
            });

            await endpoint.Process(receivedMessages, null);

            var messagesInErrorQueueCount = await CountMessagesInErrorQueue();

            Assert.AreEqual(1, messagesInErrorQueueCount);

            var poisonMessages = await RetrieveMessagesInErrorQueue();
            var message = poisonMessages.Records[0];

            Assert.IsNotNull(message);

            var messageNode = JsonNode.Parse(message.Body);

            Assert.AreEqual(messageId.ToString(), messageNode["Headers"]["NServiceBus.MessageId"].GetValue<string>());
            Assert.AreEqual(messageType, messageNode["Headers"]["NServiceBus.EnclosedMessageTypes"].GetValue<string>());
            Assert.That(message.MessageAttributes.ContainsKey("CustomAttribute"), "CustomAttribute message attribute is missing.");
        }

        [Test]
        public async Task Should_support_loading_body_from_s3()
        {
            var s3Key = Guid.NewGuid().ToString();

            await UploadMessageBodyToS3(s3Key, MessageToSend);

            var receivedMessages = await GenerateAndReceiveNativeSQSEvent(new Dictionary<string, MessageAttributeValue>
            {
                {"MessageTypeFullName", new MessageAttributeValue {DataType = "String", StringValue = typeof(Message).FullName}},
                {"S3BodyKey", new MessageAttributeValue {DataType = "String", StringValue = s3Key}},
            }, MessageToSend);

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

        public class TestContext
        {
            public string MessageReceived { get; set; }
        }

        public class Message : IMessage
        {
            public string ThisIsTheMessage { get; set; }
        }

        public class FailingMessage : IMessage
        {
            public string ThisIsTheMessage { get; set; }
        }

        public class WithEncodingHandler : IHandleMessages<Message>
        {
            public WithEncodingHandler(TestContext context) => testContext = context;

            public Task Handle(Message message, IMessageHandlerContext context)
            {
                testContext.MessageReceived = message.ThisIsTheMessage;
                return Task.CompletedTask;
            }

            TestContext testContext;
        }

        public class FailingWithEncodingHandler : IHandleMessages<FailingMessage>
        {
            public Task Handle(FailingMessage message, IMessageHandlerContext context)
            {
                throw new Exception();
            }
        }
    }
}