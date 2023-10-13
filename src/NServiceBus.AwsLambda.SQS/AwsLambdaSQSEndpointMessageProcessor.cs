namespace NServiceBus;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwsLambda.SQS;
using AwsLambda.SQS.TransportWrapper;
using Extensibility;
using Logging;
using Transport;

class AwsLambdaSQSEndpointMessageProcessor
{
    public AwsLambdaSQSEndpointMessageProcessor(IMessageProcessor pipeline, IAmazonSQS sqsClient, S3Settings s3Settings, string receiveQueueAddress, string receiveQueueUrl, string errorQueueUrl)
    {
        this.pipeline = pipeline;
        this.sqsClient = sqsClient;
        this.s3Settings = s3Settings;
        this.receiveQueueAddress = receiveQueueAddress;
        this.receiveQueueUrl = receiveQueueUrl;
        this.errorQueueUrl = errorQueueUrl;
    }

    public async Task ProcessMessage(SQSEvent.SQSMessage receivedLambdaMessage, CancellationToken token)
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
                await ProcessMessageWithInMemoryRetries(transportMessage.Headers, nativeMessageId, messageBody, receivedMessage, receivedLambdaMessage, token).ConfigureAwait(false);
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

    async Task ProcessMessageWithInMemoryRetries(Dictionary<string, string> headers, string nativeMessageId, ReadOnlyMemory<byte> body, Message nativeMessage, SQSEvent.SQSMessage nativeLambdaMessage, CancellationToken token)
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
                token.ThrowIfCancellationRequested();

                var messageContext = new MessageContext(
                    nativeMessageId,
                    new Dictionary<string, string>(headers),
                    body,
                    transportTransaction,
                    receiveQueueAddress,
                    context);

                await pipeline.PushMessage(messageContext).ConfigureAwait(false);

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
                        receiveQueueAddress,
                        context);

                    errorHandlerResult = await pipeline.PushFailedMessage(errorContext).ConfigureAwait(false);
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

    async Task DeleteMessageAndBodyIfRequired(Message message, string messageS3BodyKey)
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

    readonly JsonSerializerOptions transportMessageSerializerOptions = new()
    {
        TypeInfoResolver = TransportMessageSerializerContext.Default
    };

    readonly IMessageProcessor pipeline;
    readonly IAmazonSQS sqsClient;
    readonly S3Settings s3Settings;
    readonly string receiveQueueAddress;
    readonly string receiveQueueUrl;
    readonly string errorQueueUrl;

    static readonly ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
}