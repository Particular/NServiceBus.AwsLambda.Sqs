using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using NServiceBus.AwsLambda;
using NServiceBus.Extensibility;
using NServiceBus.Serverless;
using NServiceBus.Transport;

namespace NServiceBus
{
    public static class ServerlessEndpointExtensions
    {
        static readonly TransportTransaction transportTransaction = new TransportTransaction();

        public static Task Process(this ServerlessEndpoint<ILambdaContext> endpoint, SQSEvent @event, ILambdaContext lambdaContext)
        {
            var processTasks = new List<Task>();

            foreach (var receivedMessage in @event.Records)
            {
                var transportMessage = SimpleJson.SimpleJson.DeserializeObject<TransportMessage>(receivedMessage.Body);

                if (transportMessage.S3BodyKey != null)
                {
                    throw new Exception("S3 Body storage is not supported");
                }

                if (!IsMessageExpired(receivedMessage, transportMessage.Headers))
                {
                    var messageBody = Convert.FromBase64String(transportMessage.Body);

                    var messageContext = new MessageContext(
                        receivedMessage.MessageId,
                        transportMessage.Headers,
                        messageBody,
                        transportTransaction,
                        new CancellationTokenSource(),
                        new ContextBag());

                    processTasks.Add(endpoint.Process(messageContext, lambdaContext));
                }
            }

            return Task.WhenAll(processTasks);
        }

        static bool IsMessageExpired(SQSEvent.SQSMessage receivedMessage, Dictionary<string, string> headers)
        {
            if (!headers.TryGetValue(TransportHeaders.TimeToBeReceived, out var rawTtbr))
            {
                return false;
            }

            headers.Remove(TransportHeaders.TimeToBeReceived);
            var timeToBeReceived = TimeSpan.Parse(rawTtbr);
            if (timeToBeReceived == TimeSpan.MaxValue)
            {
                return false;
            }

            var sentDateTime = UnixEpoch.AddMilliseconds(long.Parse(receivedMessage.Attributes["SentTimestamp"], NumberFormatInfo.InvariantInfo));
            var expiresAt = sentDateTime + timeToBeReceived;
            var utcNow = DateTime.UtcNow;
            if (expiresAt > utcNow)
            {
                return false;
            }

            return true;
        }

        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
