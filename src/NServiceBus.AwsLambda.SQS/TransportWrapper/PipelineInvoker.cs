namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    sealed class PipelineInvoker : IMessageReceiver, IMessageProcessor
    {
        public PipelineInvoker(IMessageReceiver baseTransportReceiver) => this.baseTransportReceiver = baseTransportReceiver;

        public ISubscriptionManager Subscriptions => baseTransportReceiver.Subscriptions;

        public string Id => baseTransportReceiver.Id;

        public string ReceiveAddress => baseTransportReceiver.ReceiveAddress;

        Task IMessageReceiver.Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken)
        {
            this.onMessage = onMessage;
            this.onError = onError;
            var errorHandledResultTask = Task.FromResult(ErrorHandleResult.Handled);

            return baseTransportReceiver?.Initialize(limitations,
                (_, __) => Task.CompletedTask,
                (_, __) => errorHandledResultTask,
                cancellationToken) ?? Task.CompletedTask;
        }

        Task IMessageReceiver.StartReceive(CancellationToken cancellationToken) => Task.CompletedTask;

        Task IMessageReceiver.StopReceive(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ErrorHandleResult> PushFailedMessage(ErrorContext errorContext, CancellationToken cancellationToken = default) => onError(errorContext, cancellationToken);

        Task IMessageReceiver.ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PushMessage(MessageContext messageContext, CancellationToken cancellationToken = default) => onMessage.Invoke(messageContext, cancellationToken);

        OnMessage onMessage;
        OnError onError;

        readonly IMessageReceiver baseTransportReceiver;
    }
}