using System;
using System.Collections.Generic;
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
    using Amazon.S3;
    using Logging;

    public static class ServerlessEndpointExtensions
    {
        static readonly TransportTransaction transportTransaction = new TransportTransaction();

        public static Task Process(this ServerlessEndpoint<ILambdaContext> endpoint, SQSEvent @event, ILambdaContext lambdaContext)
        {
            var processTasks = new List<Task>();

            IAmazonS3 s3Client = null; 
            TransportConfiguration configuration = null;
            CancellationToken token;

            foreach (var receivedMessage in @event.Records)
            {
                processTasks.Add(ProcessMessage(endpoint, receivedMessage, lambdaContext, s3Client, configuration, token));
            }
            return Task.WhenAll(processTasks);
        }

        static async Task ProcessMessage(ServerlessEndpoint<ILambdaContext> endpoint, SQSEvent.SQSMessage receivedMessage, ILambdaContext lambdaContext, IAmazonS3 s3Client, TransportConfiguration configuration, CancellationToken token)
        {
            byte[] messageBody = null;
            TransportMessage transportMessage = null;
            Exception exception = null;
            string messageId = null;
            var isPoisonMessage = false;

            try
            {
                messageId = receivedMessage.GetMessageId();

                transportMessage = SimpleJson.SimpleJson.DeserializeObject<TransportMessage>(receivedMessage.Body);

                messageBody = await transportMessage.RetrieveBody(s3Client, configuration, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Can't deserialize. This is a poison message
                exception = ex;
                isPoisonMessage = true;
            }

            if (isPoisonMessage || messageBody == null || transportMessage == null)
            {
                LogPoisonMessage(messageId, exception);

                await MovePoisonMessageToErrorQueue(receivedMessage, messageId).ConfigureAwait(false);
                return;
            }

            var clockSkew = TimeSpan.Zero;//CorrectClockSkew.GetClockCorrectionForEndpoint(awsEndpointUrl);
            if (receivedMessage.IsMessageExpired(transportMessage.Headers, clockSkew))
            {
                return;
            }

            var messageContext = new MessageContext(
                receivedMessage.MessageId,
                transportMessage.Headers,
                messageBody,
                transportTransaction,
                new CancellationTokenSource(),
                new ContextBag());

            await endpoint.Process(messageContext, lambdaContext).ConfigureAwait(false);

            // Always delete the message from the queue.
            // If processing failed, the onError handler will have moved the message
            // to a retry queue.
            await DeleteMessageAndBodyIfRequired(receivedMessage, transportMessage.S3BodyKey).ConfigureAwait(false);

        }

        static Task DeleteMessageAndBodyIfRequired(SQSEvent.SQSMessage receivedMessage, string transportMessageS3BodyKey)
        {
            throw new NotImplementedException();
        }


        static Task MovePoisonMessageToErrorQueue(SQSEvent.SQSMessage receivedMessage, string messageId)
        {
            throw new NotImplementedException();
        }

        static void LogPoisonMessage(string messageId, Exception exception)
        {
            var logMessage = $"Treating message with {messageId} as a poison message. Moving to error queue.";

            if (exception != null)
            {
                Logger.Warn(logMessage, exception);
            }
            else
            {
                Logger.Warn(logMessage);
            }
        }

        static ILog Logger = LogManager.GetLogger(typeof(ServerlessEndpointExtensions));
    }
}
