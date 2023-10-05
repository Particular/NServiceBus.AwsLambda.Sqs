namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System;
    using System.Threading.Tasks;
    using Transport;

    class SendOnlyMessageProcessor : IMessageProcessor
    {
        //TODO should we throw an exception when the getter is called?
        public string ReceiveAddress { get; }

        public Task<ErrorHandleResult> PushFailedMessage(ErrorContext errorContext) => throw new InvalidOperationException(
                    $"This endpoint cannot process messages because it is configured in send-only mode. Remove the '{nameof(EndpointConfiguration)}.{nameof(EndpointConfiguration.SendOnly)}' configuration.'"
                    );
        public Task PushMessage(MessageContext messageContext) => throw new InvalidOperationException(
                    $"This endpoint cannot process messages because it is configured in send-only mode. Remove the '{nameof(EndpointConfiguration)}.{nameof(EndpointConfiguration.SendOnly)}' configuration.'"
                    );
    }
}