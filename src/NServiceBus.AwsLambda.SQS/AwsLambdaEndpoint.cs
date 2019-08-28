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
    using Configuration.AdvancedExtensibility;
    using Extensibility;
    using Logging;
    using Serverless;
    using SimpleJson;
    using Transport;

    /// <summary>
    /// An NServiceBus endpoint hosted in AWS Lambda which does not receive messages automatically but only handles
    /// messages explicitly passed to it by the caller.
    /// </summary>
    public class AwsLambdaSQSEndpoint : ServerlessEndpoint<ILambdaContext, SQSTriggeredEndpointConfiguration>
    {
        /// <summary>
        /// Create a new endpoint hosting in AWS Lambda.
        /// </summary>
        public AwsLambdaSQSEndpoint(Func<ILambdaContext, SQSTriggeredEndpointConfiguration> configurationFactory) : base(configurationFactory)
        {
        }

        /// <summary>
        /// Processes a messages received from an SQS trigger using the NServiceBus message pipeline.
        /// </summary>
        public async Task Process(SQSEvent @event, ILambdaContext lambdaContext)
        {
            // enforce early initialization instead of lazy during process so that the necessary clients can be created.
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            var processTasks = new List<Task>();

            foreach (var receivedMessage in @event.Records)
            {
                processTasks.Add(ProcessMessage(receivedMessage, lambdaContext, CancellationToken.None));
            }

            await Task.WhenAll(processTasks).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override async Task Initialize(SQSTriggeredEndpointConfiguration configuration)
        {
            var settingsHolder = configuration.AdvancedConfiguration.GetSettings();
            var sqsClientFactory = settingsHolder.Get<Func<IAmazonSQS>>(SettingsKeys.SqsClientFactory);
            
            sqsClient = sqsClientFactory();
            awsEndpointUrl = sqsClient.Config.DetermineServiceURL();
            queueUrl = (await sqsClient.GetQueueUrlAsync(settingsHolder.EndpointName()).ConfigureAwait(false)).QueueUrl;
            errorQueueUrl = (await sqsClient.GetQueueUrlAsync(settingsHolder.ErrorQueueAddress()).ConfigureAwait(false)).QueueUrl;

            s3BucketForLargeMessages = settingsHolder.GetOrDefault<string>(SettingsKeys.S3BucketForLargeMessages);
            if (string.IsNullOrWhiteSpace(s3BucketForLargeMessages))
            {
                return;
            }

            var s3ClientFactory = settingsHolder.Get<Func<IAmazonS3>>(SettingsKeys.S3ClientFactory);
            s3Client = s3ClientFactory();
        }

        async Task ProcessMessage(SQSEvent.SQSMessage receivedMessage, ILambdaContext lambdaContext, CancellationToken token)
        {
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

        async Task MovePoisonMessageToErrorQueue(SQSEvent.SQSMessage message, string messageId)
        {
            try
            {
                await sqsClient.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = errorQueueUrl,
                    MessageBody = message.Body,
                    MessageAttributes =
                    {
                        [Headers.MessageId] = new MessageAttributeValue
                        {
                            StringValue = messageId,
                            DataType = "String"
                        }
                    }
                }, CancellationToken.None).ConfigureAwait(false);
                // The MessageAttributes on message are read-only attributes provided by SQS
                // and can't be re-sent. Unfortunately all the SQS metadata
                // such as SentTimestamp is reset with this send.
            }
            catch (Exception ex)
            {
                Logger.Error($"Error moving poison message to error queue at url {errorQueueUrl}. Moving back to input queue.", ex);
                try
                {
                    await sqsClient.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = queueUrl,
                        ReceiptHandle = message.ReceiptHandle,
                        VisibilityTimeout = 0
                    }, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception changeMessageVisibilityEx)
                {
                    Logger.Warn($"Error returning poison message back to input queue at url {queueUrl}. Poison message will become available at the input queue again after the visibility timeout expires.", changeMessageVisibilityEx);
                }

                return;
            }

            try
            {
                await sqsClient.DeleteMessageAsync(new DeleteMessageRequest
                {
                    QueueUrl = queueUrl,
                    ReceiptHandle = message.ReceiptHandle
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error removing poison message from input queue {queueUrl}. This may cause duplicate poison messages in the error queue for this endpoint.", ex);
            }

            // If there is a message body in S3, simply leave it there
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
        string queueUrl;
        string errorQueueUrl;

        static ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
        static readonly TransportTransaction transportTransaction = new TransportTransaction();
    }
}