namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    class SendOnlyMessageReceiver : IMessageReceiver
    {
        public ISubscriptionManager Subscriptions => throw new System.NotImplementedException();

        public string Id => throw new System.NotImplementedException();

        public string ReceiveAddress => throw new System.NotImplementedException();

        public Task ChangeConcurrency(PushRuntimeSettings limitations, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();
        public Task Initialize(PushRuntimeSettings limitations, OnMessage onMessage, OnError onError, CancellationToken cancellationToken = default) => throw new System.NotImplementedException();
        public Task StartReceive(CancellationToken cancellationToken = default) => throw new System.NotImplementedException();
        public Task StopReceive(CancellationToken cancellationToken = default) => throw new System.NotImplementedException();
    }
}