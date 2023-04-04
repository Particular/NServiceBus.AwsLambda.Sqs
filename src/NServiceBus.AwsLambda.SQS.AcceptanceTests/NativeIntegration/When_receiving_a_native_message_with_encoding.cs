namespace NServiceBus.AcceptanceTests.NativeIntegration
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    using Amazon.SQS.Model;
    using Microsoft.Extensions.DependencyInjection;
    using NServiceBus.AcceptanceTests;
    using NUnit.Framework;

    class When_receiving_a_native_message_with_encoding : AwsLambdaSQSEndpointTestBase
    {
        static readonly string MessageToSend = new XDocument(new XElement("Message", new XElement("ThisIsTheMessage", "Hello!"))).ToString();

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
                // unfortunately only the message id attribute is preserved when moving to the poison queue
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
    }
}