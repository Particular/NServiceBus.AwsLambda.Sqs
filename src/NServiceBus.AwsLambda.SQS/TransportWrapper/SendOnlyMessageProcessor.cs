namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    class SendOnlyMessageProcessor : IMessageProcessor
    {
        public string ReceiveAddress => string.Empty;

        public Task<ErrorHandleResult> PushFailedMessage(ErrorContext errorContext, CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                    $"This endpoint cannot process messages because it is configured in send-only mode. Remove the '{nameof(EndpointConfiguration)}.{nameof(EndpointConfiguration.SendOnly)}' configuration.'"
                    );
        public Task PushMessage(MessageContext messageContext, CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                    $"This endpoint cannot process messages because it is configured in send-only mode. Remove the '{nameof(EndpointConfiguration)}.{nameof(EndpointConfiguration.SendOnly)}' configuration.'"
                    );
    }
}