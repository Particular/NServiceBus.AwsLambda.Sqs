namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Core;
    using Amazon.Lambda.SQSEvents;
    using Amazon.Runtime;
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
            awsEndpointUrl = sqsClient.Config.DetermineServiceURL();

            if (!string.IsNullOrWhiteSpace(Configuration.S3BucketForLargeMessages))
            {
                s3Client = new AmazonS3Client();
                s3BucketForLargeMessages = Configuration.S3BucketForLargeMessages;
            }
        }

        async Task ProcessMessage(SQSEvent.SQSMessage receivedMessage, ILambdaContext lambdaContext, CancellationToken token)
        {
            InitializeIfNeeded();

            byte[] messageBody = null;
            TransportMessage transportMessage = null;
            Exception exception = null;
            var nativeMessageId = receivedMessage.MessageId;
            string messageId = null;
            var isPoisonMessage = false;

            try
            {
                if (receivedMessage.MessageAttributes.TryGetValue(Headers.MessageId, out var messageIdAttribute))
                {
                    messageId = messageIdAttribute.StringValue;
                }
                else
                {
                    messageId = nativeMessageId;
                }

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

            if (!IsMessageExpired(receivedMessage, transportMessage.Headers, messageId, CorrectClockSkew.GetClockCorrectionForEndpoint(awsEndpointUrl)))
            {
                // here we also want to use the native message id because the core demands it like that
                await ProcessMessageWithInMemoryRetries(transportMessage.Headers, nativeMessageId, messageBody, lambdaContext, token).ConfigureAwait(false);
            }

            // Always delete the message from the queue.
            // If processing failed, the onError handler will have moved the message
            // to a retry queue.
            await DeleteMessageAndBodyIfRequired(receivedMessage, transportMessage.S3BodyKey).ConfigureAwait(false);
        }

        static bool IsMessageExpired(SQSEvent.SQSMessage receivedMessage, Dictionary<string, string> headers, string messageId, TimeSpan clockOffset)
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

            var sentDateTime = receivedMessage.GetAdjustedDateTimeFromServerSetAttributes(clockOffset);
            var expiresAt = sentDateTime + timeToBeReceived;
            var utcNow = DateTime.UtcNow;
            if (expiresAt > utcNow)
            {
                return false;
            }

            // Message has expired.
            Logger.Info($"Discarding expired message with Id {messageId}, expired {utcNow - expiresAt} ago at {expiresAt} utc.");
            return true;
        }

        async Task ProcessMessageWithInMemoryRetries(Dictionary<string, string> headers, string nativeMessageId, byte[] body, ILambdaContext lambdaContext, CancellationToken token)
        {
            var immediateProcessingAttempts = 0;
            var messageProcessedOk = false;
            var errorHandled = false;

            while (!errorHandled && !messageProcessedOk)
            {
                try
                {
                    using (var messageContextCancellationTokenSource = new CancellationTokenSource())
                    {
                        var messageContext = new MessageContext(
                            nativeMessageId,
                            new Dictionary<string, string>(headers),
                            body,
                            transportTransaction,
                            messageContextCancellationTokenSource,
                            new ContextBag());

                        await Process(messageContext, lambdaContext).ConfigureAwait(false);

                        messageProcessedOk = !messageContextCancellationTokenSource.IsCancellationRequested;
                    }
                }
                catch (Exception ex)
                    when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    immediateProcessingAttempts++;
                    ErrorHandleResult errorHandlerResult;

                    try
                    {
                        var errorContext = new ErrorContext(
                            ex,
                            new Dictionary<string, string>(headers),
                            nativeMessageId,
                            body,
                            transportTransaction,
                            immediateProcessingAttempts);

                        errorHandlerResult = await ProcessFailedMessage(errorContext, lambdaContext).ConfigureAwait(false);
                    }
                    catch (Exception onErrorEx)
                    {
                        Logger.Warn($"Failed to execute recoverability policy for message with native ID: `{nativeMessageId}`", onErrorEx);
                        throw;
                    }

                    errorHandled = errorHandlerResult == ErrorHandleResult.Handled;
                }
            }
        }

        async Task DeleteMessageAndBodyIfRequired(SQSEvent.SQSMessage message, string messageS3BodyKey)
        {
            try
            {
                // should not be cancelled
                await sqsClient.DeleteMessageAsync(awsEndpointUrl, message.ReceiptHandle, CancellationToken.None).ConfigureAwait(false);
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
        string awsEndpointUrl;

        static ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
        static readonly TransportTransaction transportTransaction = new TransportTransaction();
    }
}