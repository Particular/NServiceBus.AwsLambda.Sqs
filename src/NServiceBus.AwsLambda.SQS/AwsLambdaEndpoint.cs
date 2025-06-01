﻿namespace NServiceBus
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
    using Amazon.Runtime;
    using Amazon.SQS;
    using Amazon.SQS.Model;
    using AwsLambda.SQS;
    using AwsLambda.SQS.TransportWrapper;
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

            if (isSendOnly)
            {
                throw new InvalidOperationException(
                    $"This endpoint cannot process messages because it is configured in send-only mode. Remove the '{nameof(EndpointConfiguration)}.{nameof(EndpointConfiguration.SendOnly)}' configuration.'"
                    );
            }

            var processTasks = new List<Task>();
            foreach (var receivedMessage in @event.Records)
            {
                processTasks.Add(ProcessMessage(receivedMessage, lambdaContext, cancellationToken));
            }

            await Task.WhenAll(processTasks)
                .ConfigureAwait(false);
        }

        async Task InitializeEndpointIfNecessary(ILambdaContext executionContext, CancellationToken cancellationToken)
        {
            if (pipeline == null)
            {
                await semaphoreLock.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    if (pipeline == null)
                    {
                        var transportInfrastructure = await Initialize(executionContext, cancellationToken).ConfigureAwait(false);

                        pipeline = transportInfrastructure.PipelineInvoker;
                    }
                }
                finally
                {
                    _ = semaphoreLock.Release();
                }
            }
        }

        async Task<ServerlessTransportInfrastructure> Initialize(ILambdaContext executionContext, CancellationToken cancellationToken)
        {
            var configuration = configurationFactory(executionContext);

            sqsClient = configuration.Transport.SqsClient;
            s3Settings = configuration.Transport.S3;

            var getQueueUrlRequest = new GetQueueUrlRequest
            {
                QueueName = "any-queue-name" // This is a dummy value resolve the endpoint URL from the SQS client.
            };
            endpointUrl = sqsClient.DetermineServiceOperationEndpoint(getQueueUrlRequest).URL;

            var serverlessTransport = new ServerlessTransport(configuration.Transport);
            configuration.EndpointConfiguration.UseTransport(serverlessTransport);

            endpoint = await Endpoint.Start(configuration.EndpointConfiguration, cancellationToken).ConfigureAwait(false);

            var transportInfrastructure = serverlessTransport.GetTransportInfrastructure(endpoint);
            isSendOnly = transportInfrastructure.IsSendOnly;

            if (!isSendOnly)
            {
                receiveQueueAddress = transportInfrastructure.PipelineInvoker.ReceiveAddress;
                receiveQueueUrl = await GetQueueUrl(receiveQueueAddress, cancellationToken).ConfigureAwait(false);
                errorQueueUrl = await GetQueueUrl(transportInfrastructure.ErrorQueueAddress, cancellationToken).ConfigureAwait(false);
            }

            return transportInfrastructure;
        }

        /// <inheritdoc />
        public async Task Send(object message, SendOptions options, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken).ConfigureAwait(false);

            await endpoint.Send(message, options, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task Send(object message, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
            => Send(message, new SendOptions(), lambdaContext, cancellationToken);

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, SendOptions options, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Send(messageConstructor, options, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await Send(messageConstructor, new SendOptions(), lambdaContext, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, PublishOptions options, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Publish(message, options, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, PublishOptions options, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Publish(messageConstructor, options, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Publish(message, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Publish(messageConstructor, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, SubscribeOptions options, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Subscribe(eventType, options, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Subscribe(eventType, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, UnsubscribeOptions options, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType, options, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType, cancellationToken)
                .ConfigureAwait(false);
        }

        async Task<string> GetQueueUrl(string queueName, CancellationToken cancellationToken)
        {
            try
            {
                return (await sqsClient.GetQueueUrlAsync(queueName, cancellationToken).ConfigureAwait(false)).QueueUrl;
            }
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                Logger.Error($"Failed to obtain the queue URL for queue {queueName} (derived from configured name {queueName}).", ex);
                throw;
            }
        }

        async Task ProcessMessage(SQSEvent.SQSMessage receivedLambdaMessage, ILambdaContext lambdaContext, CancellationToken cancellationToken)
        {
            var arrayPool = ArrayPool<byte>.Shared;
            ReadOnlyMemory<byte> messageBody = null;
            byte[] messageBodyBuffer = null;
            TransportMessage transportMessage = null;
            Exception exception = null;
            var receivedMessage = receivedLambdaMessage.ToMessage();
            var nativeMessageId = receivedMessage.MessageId;
            string messageId = null;
            var isPoisonMessage = false;

            try
            {
                try
                {
                    messageId = receivedMessage.MessageAttributes?.TryGetValue(Headers.MessageId, out var messageIdAttribute) is true ? messageIdAttribute.StringValue : nativeMessageId;

                    if (receivedMessage.MessageAttributes?.TryGetValue(TransportHeaders.Headers, out var headersAttribute) is true)
                    {
                        transportMessage = new TransportMessage
                        {
                            Headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersAttribute.StringValue) ?? [],
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
                        if (receivedMessage.MessageAttributes?.TryGetValue(TransportHeaders.MessageTypeFullName,
                                out var enclosedMessageType) is true)
                        {
                            var headers = new Dictionary<string, string>
                            {
                                { Headers.MessageId, messageId },
                                { Headers.EnclosedMessageTypes, enclosedMessageType.StringValue },
                                {
                                    TransportHeaders.MessageTypeFullName, enclosedMessageType.StringValue
                                } // we're copying over the value of the native message attribute into the headers, converting this into a nsb message
                            };

                            MessageAttributeValue s3BodyKey = null;
                            if (receivedMessage.MessageAttributes?.TryGetValue(TransportHeaders.S3BodyKey,
                                    out s3BodyKey) is true)
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

                    (messageBody, messageBodyBuffer) = await transportMessage.RetrieveBody(messageId, s3Settings, arrayPool, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                {
                    // Can't deserialize. This is a poison message
                    exception = ex;
                    isPoisonMessage = true;
                }

                if (isPoisonMessage || transportMessage == null)
                {
                    LogPoisonMessage(messageId, exception);

                    await MovePoisonMessageToErrorQueue(receivedMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var clockCorrection = CorrectClockSkew.GetClockCorrectionForEndpoint(endpointUrl);
                if (!IsMessageExpired(receivedMessage, transportMessage.Headers, messageId, clockCorrection))
                {
                    // here we also want to use the native message id because the core demands it like that
                    await ProcessMessageWithInMemoryRetries(transportMessage.Headers, nativeMessageId, messageBody, receivedMessage, receivedLambdaMessage, lambdaContext, cancellationToken).ConfigureAwait(false);
                }

                // Always delete the message from the queue.
                // If processing failed, the onError handler will have moved the message
                // to a retry queue.
                await DeleteMessageAndBodyIfRequired(receivedMessage, transportMessage.S3BodyKey, cancellationToken).ConfigureAwait(false);
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
            if (!headers.Remove(TransportHeaders.TimeToBeReceived, out var rawTtbr))
            {
                return false;
            }

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

        async Task ProcessMessageWithInMemoryRetries(Dictionary<string, string> headers, string nativeMessageId, ReadOnlyMemory<byte> body, Message nativeMessage, SQSEvent.SQSMessage nativeLambdaMessage, ILambdaContext lambdaContext, CancellationToken cancellationToken)
        {
            var immediateProcessingAttempts = 0;
            var errorHandled = false;

            while (!errorHandled)
            {
                // set the native message on the context for advanced usage scenarios
                var context = new ContextBag();
                context.Set(nativeMessage);
                context.Set(nativeLambdaMessage);

                // We add it to the transport transaction to make it available in dispatching scenarios so we copy over message attributes when moving messages to the error/audit queue
                var transportTransaction = new TransportTransaction();
                transportTransaction.Set(nativeMessage);
                transportTransaction.Set(nativeLambdaMessage);
                transportTransaction.Set("IncomingMessageId", headers[Headers.MessageId]);

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var messageContext = new MessageContext(
                        nativeMessageId,
                        new Dictionary<string, string>(headers),
                        body,
                        transportTransaction,
                        receiveQueueAddress,
                        context);

                    await Process(messageContext, lambdaContext, cancellationToken).ConfigureAwait(false);

                    return;
                }
                catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
                {
                    immediateProcessingAttempts++;

                    var errorContext = new ErrorContext(
                        ex,
                        new Dictionary<string, string>(headers),
                        nativeMessageId,
                        body,
                        transportTransaction,
                        immediateProcessingAttempts,
                        receiveQueueAddress,
                        context);

                    var errorHandlerResult = await ProcessFailedMessage(errorContext, lambdaContext, nativeMessageId, cancellationToken).ConfigureAwait(false);

                    errorHandled = errorHandlerResult == ErrorHandleResult.Handled;
                }
            }
        }

        async Task Process(MessageContext messageContext, ILambdaContext executionContext, CancellationToken cancellationToken)
        {
            await InitializeEndpointIfNecessary(executionContext, cancellationToken)
                .ConfigureAwait(false);

            await pipeline.PushMessage(messageContext, cancellationToken)
                .ConfigureAwait(false);
        }

        async Task<ErrorHandleResult> ProcessFailedMessage(ErrorContext errorContext, ILambdaContext executionContext, string nativeMessageId, CancellationToken cancellationToken)
        {
            try
            {
                await InitializeEndpointIfNecessary(executionContext, cancellationToken)
                    .ConfigureAwait(false);

                return await pipeline.PushFailedMessage(errorContext, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception onErrorEx) when (!onErrorEx.IsCausedBy(cancellationToken))
            {
                Logger.Warn($"Failed to execute recoverability policy for message with native ID: `{nativeMessageId}`", onErrorEx);
                throw;
            }
        }

        async Task DeleteMessageAndBodyIfRequired(Message message, string messageS3BodyKey, CancellationToken cancellationToken)
        {
            try
            {
                // should not be cancelled
                await sqsClient.DeleteMessageAsync(receiveQueueUrl, message.ReceiptHandle, CancellationToken.None)
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
                    ?.ToDictionary(pair => pair.Key, messageAttribute => messageAttribute.Value);

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
            catch (Exception ex) when (!ex.IsCausedBy(cancellationToken))
            {
                Logger.Error($"Error moving poison message to error queue at url {errorQueueUrl}. Moving back to input queue.", ex);
                try
                {
                    await sqsClient.ChangeMessageVisibilityAsync(new ChangeMessageVisibilityRequest
                    {
                        QueueUrl = receiveQueueUrl,
                        ReceiptHandle = message.ReceiptHandle,
                        VisibilityTimeout = 0
                    }, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception changeMessageVisibilityEx)
                {
                    Logger.Warn($"Error returning poison message back to input queue at url {receiveQueueUrl}. Poison message will become available at the input queue again after the visibility timeout expires.", changeMessageVisibilityEx);
                }

                throw;
            }

            try
            {
                await sqsClient.DeleteMessageAsync(new DeleteMessageRequest
                {
                    QueueUrl = receiveQueueUrl,
                    ReceiptHandle = message.ReceiptHandle
                }, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error removing poison message from input queue {receiveQueueAddress}. This may cause duplicate poison messages in the error queue for this endpoint.", ex);
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

        bool isSendOnly;
        readonly Func<ILambdaContext, AwsLambdaSQSEndpointConfiguration> configurationFactory;
        readonly SemaphoreSlim semaphoreLock = new(initialCount: 1, maxCount: 1);
        readonly JsonSerializerOptions transportMessageSerializerOptions = new()
        {
            TypeInfoResolver = TransportMessageSerializerContext.Default
        };
        IMessageProcessor pipeline;
        IEndpointInstance endpoint;
        IAmazonSQS sqsClient;
        S3Settings s3Settings;
        string receiveQueueAddress;
        string receiveQueueUrl;
        string errorQueueUrl;
        string endpointUrl;

        static readonly ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
    }
}