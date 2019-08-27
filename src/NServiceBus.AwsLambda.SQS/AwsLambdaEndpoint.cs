namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Core;
    using Amazon.Lambda.SQSEvents;
    using Amazon.S3;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using AwsLambda;
    using Extensibility;
    using Logging;
    using Serverless;
    using SimpleJson;
    using Transport;

    public class AwsLambdaSQSEndpoint : ServerlessEndpoint<ILambdaContext, SQSTriggeredEndpointConfiguration>
    {
        public AwsLambdaSQSEndpoint(Func<ILambdaContext, SQSTriggeredEndpointConfiguration> configurationFactory) : base(configurationFactory)
        {
        }

        public Task Process(SQSEvent @event, ILambdaContext lambdaContext)
        {
            var processTasks = new List<Task>();

            foreach (var receivedMessage in @event.Records)
            {
                processTasks.Add(ProcessMessage(receivedMessage, lambdaContext, CancellationToken.None));
            }

            return Task.WhenAll(processTasks);
        }

        void InitializeIfNeeded()
        {

            if (sqsClient == null)
            {
                return;
            }

            sqsClient = new AmazonSQSClient();

            if (!string.IsNullOrWhiteSpace(Configuration.S3BucketForLargeMessages))
            {
                s3Client = new AmazonS3Client();
            }
        }

        async Task ProcessMessage(SQSEvent.SQSMessage receivedMessage, ILambdaContext lambdaContext, CancellationToken token)
        {
            InitializeIfNeeded();

            byte[] messageBody = null;
            TransportMessage transportMessage = null;
            Exception exception = null;
            string messageId = null;
            var isPoisonMessage = false;

            try
            {
                messageId = receivedMessage.GetMessageId();

                transportMessage = SimpleJson.DeserializeObject<TransportMessage>(receivedMessage.Body);

                messageBody = await transportMessage.RetrieveBody(s3Client, s3BucketForLargeMessages, token).ConfigureAwait(false);
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

            var clockSkew = TimeSpan.Zero; //CorrectClockSkew.GetClockCorrectionForEndpoint(awsEndpointUrl);
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

            while (true)
            {
                try
                {
                    await Process(messageContext, lambdaContext).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var handleResult = await ProcessFailedMessage(messageContext, ex, 0, lambdaContext).ConfigureAwait(false);

                    if (handleResult == ErrorHandleResult.Handled)
                    {
                        break;
                    }
                }
            }

            // Always delete the message from the queue.
            // If processing failed, the onError handler will have moved the message
            // to a retry queue.
            await DeleteMessageAndBodyIfRequired(receivedMessage, transportMessage.S3BodyKey).ConfigureAwait(false);
        }

        async Task DeleteMessageAndBodyIfRequired(SQSEvent.SQSMessage message, string messageS3BodyKey)
        {
            try
            {
                // should not be cancelled
                await sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ReceiptHandleIsInvalidException ex)
            {
                Logger.Info($"Message receipt handle {message.ReceiptHandle} no longer valid.", ex);
                return; // if another receiver fetches the data from S3
            }

            if (!string.IsNullOrEmpty(messageS3BodyKey))
            {
                Logger.Info($"Message body data with key '{messageS3BodyKey}' will be aged out by the S3 lifecycle policy when the TTL expires.");
            }
        }

        static Task MovePoisonMessageToErrorQueue(SQSEvent.SQSMessage receivedMessage, string messageId)
        {
            throw new NotImplementedException(); // move to error is the sqs transport behaviour, but we don't have access to the error queue. Need to decide what to do here.
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

        IAmazonSQS sqsClient;
        IAmazonS3 s3Client;
        string s3BucketForLargeMessages;
        string queueUrl;

        static ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
        static readonly TransportTransaction transportTransaction = new TransportTransaction();
    }
}