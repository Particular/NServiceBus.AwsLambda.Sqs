namespace NServiceBus
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Core;
    using Amazon.Lambda.SQSEvents;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using AwsLambda.SQS;
    using AwsLambda.SQS.TransportWrapper;
    using Configuration.AdvancedExtensibility;
    using Extensibility;
    using Logging;
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
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            var processTasks = new List<Task>();

            foreach (var receivedMessage in @event.Records)
            {
                var message = MessageTypeAdapter.ToMessage(receivedMessage);
                processTasks.Add(ProcessMessage(message, lambdaContext, cancellationToken));
            }

            await Task.WhenAll(processTasks)
                .ConfigureAwait(false);
        }

        async Task InitializeEndpointIfNecessary(ILambdaContext executionContext, CancellationToken token = default)
        {
            if (pipeline == null)
            {
                await semaphoreLock.WaitAsync(token)
                    .ConfigureAwait(false);
                try
                {
                    if (pipeline == null)
                    {
                        var configuration = configurationFactory(executionContext);

                        var serverlessTransport = await Initialize(configuration)
                            .ConfigureAwait(false);

                        endpoint = await Endpoint.Start(configuration.EndpointConfiguration, token)
                            .ConfigureAwait(false);

                        pipeline = serverlessTransport.PipelineInvoker;
                    }
                }
                finally
                {
                    _ = semaphoreLock.Release();
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
            => Send(message, new SendOptions(), lambdaContext);

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, SendOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Send(messageConstructor, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await Send(messageConstructor, new SendOptions(), lambdaContext)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, PublishOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(message, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, PublishOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(messageConstructor, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(message)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(messageConstructor)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, SubscribeOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Subscribe(eventType, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Subscribe(eventType)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, UnsubscribeOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType)
                .ConfigureAwait(false);
        }

        async Task<ServerlessTransport> Initialize(AwsLambdaSQSEndpointConfiguration configuration)
        {
            var settingsHolder = configuration.AdvancedConfiguration.GetSettings();

            sqsClient = configuration.Transport.SqsClient;

            queueUrl = await GetQueueUrl(settingsHolder.EndpointName())
                .ConfigureAwait(false);
            errorQueueUrl = await GetQueueUrl(settingsHolder.ErrorQueueAddress())
                .ConfigureAwait(false);

            s3Settings = configuration.Transport.S3;

            var serverlessTransport = new ServerlessTransport(configuration.Transport);
            configuration.EndpointConfiguration.UseTransport(serverlessTransport);

            return serverlessTransport;
        }

        async Task<string> GetQueueUrl(string queueName)
        {
            var sanitizedQueueName = QueueNameHelper.GetSanitizedQueueName(queueName);
            try
            {
                return (await sqsClient.GetQueueUrlAsync(sanitizedQueueName).ConfigureAwait(false)).QueueUrl;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to obtain the queue URL for queue {sanitizedQueueName} (derived from configured name {queueName}).", e);
                throw;
            }
        }

        async Task ProcessMessage(Message receivedMessage, ILambdaContext lambdaContext, CancellationToken token)
        {
            var arrayPool = ArrayPool<byte>.Shared;
            ReadOnlyMemory<byte> messageBody = null;
            byte[] messageBodyBuffer = null;
            TransportMessage transportMessage = null;
            Exception exception = null;
            var nativeMessageId = receivedMessage.MessageId;
            string messageId = null;
            var isPoisonMessage = false;

            try
            {
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

                    if (receivedMessage.MessageAttributes.TryGetValue(TransportHeaders.Headers, out var headersAttribute))
                    {
                        transportMessage = new TransportMessage
                        {
                            Headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersAttribute.StringValue) ?? new Dictionary<string, string>(),
                            Body = receivedMessage.Body
                        };
                        transportMessage.Headers[Headers.MessageId] = messageId;
                        if (receivedMessage.MessageAttributes.TryGetValue(TransportHeaders.S3BodyKey, out var s3BodyKey))
                        {
                            transportMessage.Headers[TransportHeaders.S3BodyKey] = s3BodyKey.StringValue;
                            transportMessage.S3BodyKey = s3BodyKey.StringValue;
                        }
                    }
                    else
                    {
                        // When the MessageTypeFullName attribute is available, we're assuming native integration
                        if (receivedMessage.MessageAttributes.TryGetValue(TransportHeaders.MessageTypeFullName,
                                out var enclosedMessageType))
                        {
                            var headers = new Dictionary<string, string>
                            {
                                { Headers.MessageId, messageId },
                                { Headers.EnclosedMessageTypes, enclosedMessageType.StringValue },
                                {
                                    TransportHeaders.MessageTypeFullName, enclosedMessageType.StringValue
                                } // we're copying over the value of the native message attribute into the headers, converting this into a nsb message
                            };

                            if (receivedMessage.MessageAttributes.TryGetValue(TransportHeaders.S3BodyKey,
                                    out var s3BodyKey))
                            {
                                headers.Add(TransportHeaders.S3BodyKey, s3BodyKey.StringValue);
                            }

                            transportMessage = new TransportMessage
                            {
                                Headers = headers,
                                S3BodyKey = s3BodyKey?.StringValue,
                                Body = receivedMessage.Body
                            };
                        }
                        else
                        {
                            transportMessage = JsonSerializer.Deserialize<TransportMessage>(receivedMessage.Body,
                                transportMessageSerializerOptions);
                        }
                    }

                    (messageBody, messageBodyBuffer) = await transportMessage.RetrieveBody(messageId, s3Settings, arrayPool, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(token))
                {
                    // Can't deserialize. This is a poison message
                    exception = ex;
                    isPoisonMessage = true;
                }

                if (isPoisonMessage || transportMessage == null)
                {
                    LogPoisonMessage(messageId, exception);

                    await MovePoisonMessageToErrorQueue(receivedMessage, token).ConfigureAwait(false);
                    return;
                }

                if (!IsMessageExpired(receivedMessage, transportMessage.Headers, messageId, sqsClient.Config.ClockOffset))
                {
                    // here we also want to use the native message id because the core demands it like that
                    await ProcessMessageWithInMemoryRetries(transportMessage.Headers, nativeMessageId, messageBody, receivedMessage, lambdaContext, token).ConfigureAwait(false);
                }

                // Always delete the message from the queue.
                // If processing failed, the onError handler will have moved the message
                // to a retry queue.
                await DeleteMessageAndBodyIfRequired(receivedMessage, transportMessage.S3BodyKey).ConfigureAwait(false);
            }
            finally
            {
                if (messageBodyBuffer != null)
                {
                    arrayPool.Return(messageBodyBuffer, clearArray: true);
                }
            }
        }

        static bool IsMessageExpired(Message receivedMessage, Dictionary<string, string> headers, string messageId, TimeSpan clockOffset)
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

            var sentDateTime = receivedMessage.GetAdjustedDateTimeFromServerSetAttributes("SentTimestamp", clockOffset);
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

        async Task ProcessMessageWithInMemoryRetries(Dictionary<string, string> headers, string nativeMessageId, ReadOnlyMemory<byte> body, Message nativeMessage, ILambdaContext lambdaContext, CancellationToken token)
        {
            var immediateProcessingAttempts = 0;
            var errorHandled = false;

            while (!errorHandled)
            {
                // set the native message on the context for advanced usage scenario's
                var context = new ContextBag();
                context.Set(nativeMessage);

                // We add it to the transport transaction to make it available in dispatching scenario's so we copy over message attributes when moving messages to the error/audit queue
                var transportTransaction = new TransportTransaction();
                transportTransaction.Set(nativeMessage);
                transportTransaction.Set("IncomingMessageId", headers[Headers.MessageId]);

                try
                {
                    token.ThrowIfCancellationRequested();

                    var messageContext = new MessageContext(
                        nativeMessageId,
                        new Dictionary<string, string>(headers),
                        body,
                        transportTransaction,
                        queueUrl,
                        context);

                    await Process(messageContext, lambdaContext, token).ConfigureAwait(false);

                    return;
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
                            context);

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
            await InitializeEndpointIfNecessary(executionContext, cancellationToken)
                .ConfigureAwait(false);
            await pipeline.PushMessage(messageContext)
                .ConfigureAwait(false);
        }

        async Task<ErrorHandleResult> ProcessFailedMessage(ErrorContext errorContext, ILambdaContext executionContext)
        {
            await InitializeEndpointIfNecessary(executionContext)
                .ConfigureAwait(false);

            return await pipeline.PushFailedMessage(errorContext)
                .ConfigureAwait(false);
        }

        async Task DeleteMessageAndBodyIfRequired(Message message, string messageS3BodyKey)
        {
            try
            {
                // should not be cancelled
                await sqsClient.DeleteMessageAsync(queueUrl, message.ReceiptHandle, CancellationToken.None)
                    .ConfigureAwait(false);
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

        async Task MovePoisonMessageToErrorQueue(Message message, CancellationToken cancellationToken)
        {
            try
            {
                // Ok to use LINQ here since this is not really a hot path
                var messageAttributeValues = message.MessageAttributes
                    .ToDictionary(pair => pair.Key, messageAttribute => messageAttribute.Value);

                await sqsClient.SendMessageAsync(new SendMessageRequest
                {
                    QueueUrl = errorQueueUrl,
                    MessageBody = message.Body,
                    MessageAttributes = messageAttributeValues
                }, cancellationToken)
                    .ConfigureAwait(false);
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
                    }, CancellationToken.None)
                        .ConfigureAwait(false);
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
                }, CancellationToken.None)
                    .ConfigureAwait(false);
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
        readonly SemaphoreSlim semaphoreLock = new(initialCount: 1, maxCount: 1);
        readonly JsonSerializerOptions transportMessageSerializerOptions = new()
        {
            TypeInfoResolver = TransportMessageSerializerContext.Default
        };
        PipelineInvoker pipeline;
        IEndpointInstance endpoint;
        IAmazonSQS sqsClient;
        S3Settings s3Settings;
        string queueUrl;
        string errorQueueUrl;

        static readonly ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
    }
}