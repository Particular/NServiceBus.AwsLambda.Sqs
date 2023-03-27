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
    using AwsLambda.SQS;
    using AwsLambda.SQS.TransportWrapper;
    using Configuration.AdvancedExtensibility;
    using Extensibility;
    using Logging;
    using SimpleJson;
    using Transport;

    /// <summary>
    /// An NServiceBus endpoint hosted in AWS Lambda which does not receive messages automatically but only handles
    /// messages explicitly passed to it by the caller.
    /// </summary>
    public class AwsLambdaSQSEndpoint : IAwsLambdaSQSEndpoint
    {
        /// <summary>
        /// Create a new endpoint hosting in AWS Lambda.
        /// </summary>
        public AwsLambdaSQSEndpoint(Func<ILambdaContext, AwsLambdaSQSEndpointConfiguration> configurationFactory)
        {
            this.configurationFactory = configurationFactory;
        }

        /// <summary>
        /// Processes a messages received from an SQS trigger using the NServiceBus message pipeline.
        /// </summary>
        public async Task Process(SQSEvent @event, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            // enforce early initialization instead of lazy during process so that the necessary clients can be created.
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken).ConfigureAwait(false);

            var processTasks = new List<Task>();

            foreach (var receivedMessage in @event.Records)
            {
                processTasks.Add(ProcessMessage(receivedMessage, lambdaContext, cancellationToken));
            }

            await Task.WhenAll(processTasks).ConfigureAwait(false);
        }

        async Task InitializeEndpointIfNecessary(ILambdaContext executionContext, CancellationToken token = default)
        {
            if (pipeline == null)
            {
                await semaphoreLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (pipeline == null)
                    {
                        var configuration = configurationFactory(executionContext);
                        var serverlessTransport = configuration.MakeServerless();

                        await Initialize(configuration).ConfigureAwait(false);
                        LogManager.GetLogger("Previews").Info("NServiceBus.AwsLambda.SQS is a preview package. Preview packages are licensed separately from the rest of the Particular Software platform and have different support guarantees. You can view the license at https://particular.net/eula/previews and the support policy at https://docs.particular.net/previews/support-policy. Customer adoption drives whether NServiceBus.AwsLambda.SQS will be incorporated into the Particular Software platform. Let us know you are using it, if you haven't already, by emailing us at support@particular.net.");

                        endpoint = await Endpoint.Start(configuration.EndpointConfiguration, token).ConfigureAwait(false);

                        pipeline = serverlessTransport.PipelineInvoker;
                    }
                }
                finally
                {
                    semaphoreLock.Release();
                }
            }
        }

        /// <inheritdoc />
        public async Task Send(object message, SendOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Send(message, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task Send(object message, ILambdaContext lambdaContext)
        {
            return Send(message, new SendOptions(), lambdaContext);
        }

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, SendOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Send(messageConstructor, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await Send(messageConstructor, new SendOptions(), lambdaContext).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, PublishOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Publish(message, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, PublishOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Publish(messageConstructor, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Publish(message).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Publish(messageConstructor).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, SubscribeOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Subscribe(eventType, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Subscribe(eventType).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, UnsubscribeOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType).ConfigureAwait(false);
        }

        async Task Initialize(AwsLambdaSQSEndpointConfiguration configuration)
        {
            var settingsHolder = configuration.AdvancedConfiguration.GetSettings();

            sqsClient = configuration.Transport.SqsClient;
            awsEndpointUrl = sqsClient.Config.DetermineServiceURL();

            queueUrl = await GetQueueUrl(settingsHolder.EndpointName()).ConfigureAwait(false);
            errorQueueUrl = await GetQueueUrl(settingsHolder.ErrorQueueAddress()).ConfigureAwait(false);

            s3BucketForLargeMessages = configuration.Transport.S3?.BucketName;
            if (string.IsNullOrWhiteSpace(s3BucketForLargeMessages))
            {
                return;
            }
            s3Client = configuration.Transport.S3?.S3Client;
        }

        async Task<string> GetQueueUrl(string queueName)
        {
            var sanitizedErrorQueueName = QueueNameHelper.GetSanitizedQueueName(queueName);
            try
            {
                return (await sqsClient.GetQueueUrlAsync(sanitizedErrorQueueName).ConfigureAwait(false)).QueueUrl;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to obtain the queue URL for queue {sanitizedErrorQueueName} (derived from configured name {queueName}).", e);
                throw;
            }
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
                throw;
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

        async Task ProcessMessageWithInMemoryRetries(Dictionary<string, string> headers, string nativeMessageId, ReadOnlyMemory<byte> body, ILambdaContext lambdaContext, CancellationToken token)
        {
            var immediateProcessingAttempts = 0;
            var errorHandled = false;
            var messageProcessedOk = false;

            while (!errorHandled && !messageProcessedOk)
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    var messageContext = new MessageContext(
                        nativeMessageId,
                        new Dictionary<string, string>(headers),
                        body,
                        transportTransaction,
                        queueUrl,
                        new ContextBag());

                    await Process(messageContext, lambdaContext, token).ConfigureAwait(false);

                    messageProcessedOk = true;
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
                            immediateProcessingAttempts,
                            queueUrl,
                            new ContextBag()
                            );

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

        async Task Process(MessageContext messageContext, ILambdaContext executionContext, CancellationToken cancellationToken)
        {
            await InitializeEndpointIfNecessary(executionContext, cancellationToken).ConfigureAwait(false);
            await pipeline.PushMessage(messageContext).ConfigureAwait(false);
        }

        async Task<ErrorHandleResult> ProcessFailedMessage(ErrorContext errorContext, ILambdaContext executionContext)
        {
            await InitializeEndpointIfNecessary(executionContext).ConfigureAwait(false);

            return await pipeline.PushFailedMessage(errorContext).ConfigureAwait(false);
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

                throw;
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

        readonly Func<ILambdaContext, AwsLambdaSQSEndpointConfiguration> configurationFactory;
        readonly SemaphoreSlim semaphoreLock = new SemaphoreSlim(initialCount: 1, maxCount: 1);
        PipelineInvoker pipeline;
        IEndpointInstance endpoint;
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