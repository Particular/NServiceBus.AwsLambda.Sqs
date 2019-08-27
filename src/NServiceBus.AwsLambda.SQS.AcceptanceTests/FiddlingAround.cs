namespace NServiceBus.AwsLambda.SQS.AcceptanceTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using NUnit.Framework;

    [TestFixture]
    public class FiddlingAround
    {
        [Test]
        public async Task Definitely()
        {
            var queueNamePrefix = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();
            var queueName = $"{queueNamePrefix}lambdamessagehandling";
            var client = new AmazonSQSClient();
            var createdQueue = await client.CreateQueueAsync(queueName);
            var errorQueueName = $"{queueNamePrefix}error";
            var createdErrorQueue = await client.CreateQueueAsync(errorQueueName);
            try
            {
                var messageIds = new[] {Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()};
                var batchRequests = new List<SendMessageBatchRequestEntry>();
                foreach (var messageId in messageIds)
                {
                    batchRequests.Add(new SendMessageBatchRequestEntry(messageId.ToString(), bodyTemplate
                        .Replace("{MESSAGE_ID}", messageId.ToString())
                        .Replace("{MESSAGE_TYPE}", typeof(MyMessage).AssemblyQualifiedName)));
                }

                var batchRequest = new SendMessageBatchRequest(createdQueue.QueueUrl, batchRequests);
                await client.SendMessageBatchAsync(batchRequest);

                var receiveRequest = new ReceiveMessageRequest(createdQueue.QueueUrl)
                {
                    MaxNumberOfMessages = messageIds.Length
                };
                var receivedMessages = await client.ReceiveMessageAsync(receiveRequest);

                var endpoint = new AwsLambdaSQSEndpoint(ctx =>
                {
                    var configuration = new SQSTriggeredEndpointConfiguration(queueName);
                    configuration.AdvancedConfiguration.SendFailedMessagesTo(errorQueueName);
                    return configuration;
                });

                await endpoint.Process(receivedMessages.ToSQSEvent(), null);

                var messagesInErrorQueueResponse = await client.ReceiveMessageAsync(new ReceiveMessageRequest(createdErrorQueue.QueueUrl)
                {
                    MaxNumberOfMessages = 10
                });

                Assert.AreEqual(0, messagesInErrorQueueResponse.Messages.Count);
            }
            finally
            {
                await client.DeleteQueueAsync(createdQueue.QueueUrl);
                await client.DeleteQueueAsync(createdErrorQueue.QueueUrl);
            }
        }

        [Test]
        public async Task ErrorQueue()
        {
            var queueNamePrefix = Path.GetFileNameWithoutExtension(Path.GetRandomFileName()).ToLowerInvariant();
            var queueName = $"{queueNamePrefix}lambdamessagehandling";
            var client = new AmazonSQSClient();
            var createdQueue = await client.CreateQueueAsync(queueName);
            var errorQueueName = $"{queueNamePrefix}error";
            var createdErrorQueue = await client.CreateQueueAsync(errorQueueName);
            try
            {
                var messageIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
                var batchRequests = new List<SendMessageBatchRequestEntry>();
                foreach (var messageId in messageIds)
                {
                    batchRequests.Add(new SendMessageBatchRequestEntry(messageId.ToString(), bodyTemplate
                        .Replace("{MESSAGE_ID}", messageId.ToString())
                        .Replace("{MESSAGE_TYPE}", typeof(MyAlwaysThrowingMessage).AssemblyQualifiedName)));
                }

                var batchRequest = new SendMessageBatchRequest(createdQueue.QueueUrl, batchRequests);
                await client.SendMessageBatchAsync(batchRequest);

                // Figure a better way
                await Task.Delay(TimeSpan.FromSeconds(30));

                var receiveRequest = new ReceiveMessageRequest(createdQueue.QueueUrl)
                {
                    MaxNumberOfMessages = messageIds.Length
                };
                var receivedMessages = await client.ReceiveMessageAsync(receiveRequest);

                var endpoint = new AwsLambdaSQSEndpoint(ctx =>
                {
                    var configuration = new SQSTriggeredEndpointConfiguration(queueName);
                    configuration.AdvancedConfiguration.SendFailedMessagesTo(errorQueueName);
                    return configuration;
                });

                await endpoint.Process(receivedMessages.ToSQSEvent(), null);

                // Figure a better way
                await Task.Delay(TimeSpan.FromSeconds(30));

                var messagesInErrorQueueResponse = await client.ReceiveMessageAsync(new ReceiveMessageRequest(createdErrorQueue.QueueUrl)
                {
                    MaxNumberOfMessages = 10
                });

                Assert.AreEqual(3, messagesInErrorQueueResponse.Messages.Count);
            }
            finally
            {
                await client.DeleteQueueAsync(createdQueue.QueueUrl);
                await client.DeleteQueueAsync(createdErrorQueue.QueueUrl);
            }
        }

        string bodyTemplate = "{\"Headers\":{\"NServiceBus.MessageId\":\"{MESSAGE_ID}\",\"NServiceBus.MessageIntent\":\"Send\",\"NServiceBus.ConversationId\":\"1a03df5a-cc4b-4917-9e57-aa7f01608141\",\"NServiceBus.CorrelationId\":\"94190b19-6914-48f8-ab31-aa7f01608130\",\"NServiceBus.OriginatingMachine\":\"BOBDELLLAPTOP\",\"NServiceBus.OriginatingEndpoint\":\"SendOnly\",\"$.diagnostics.originating.hostid\":\"e2732075222241849a9f8c5b8346f25b\",\"NServiceBus.ContentType\":\"text/xml\",\"NServiceBus.EnclosedMessageTypes\":\"{MESSAGE_TYPE}\",\"NServiceBus.Version\":\"7.3.0\",\"NServiceBus.TimeSent\":\"2019-07-03 21:23:25:906269 Z\"},\"Body\":\"PD94bWwgdmVyc2lvbj0iMS4wIj8+PE15TWVzc2FnZSB4bWxuczp4c2k9Imh0dHA6Ly93d3cudzMub3JnLzIwMDEvWE1MU2NoZW1hLWluc3RhbmNlIiB4bWxuczp4c2Q9Imh0dHA6Ly93d3cudzMub3JnLzIwMDEvWE1MU2NoZW1hIiB4bWxucz0iaHR0cDovL3RlbXB1cmkubmV0LyI+PElkPjRlMmIwZjNiLTM5ZTktNDYxNy05OTAwLTI1NWNjMzFhOThjMzwvSWQ+PC9NeU1lc3NhZ2U+\",\"S3BodyKey\":null}";
    }
}