namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    class ServerlessTransportInfrastructure<TBaseInfrastructure> : TransportInfrastructure where TBaseInfrastructure : TransportDefinition
    {
        public ServerlessTransportInfrastructure(TransportInfrastructure baseTransportInfrastructure)
        {
            this.baseTransportInfrastructure = baseTransportInfrastructure;
            Dispatcher = baseTransportInfrastructure.Dispatcher;
            Receivers = baseTransportInfrastructure.Receivers.ToDictionary(r => r.Key, r => (IMessageReceiver)new PipelineInvoker(r.Value));
        }

        //TODO: Is this still necessary?
        //support ReceiveOnly so that we can use immediate retries
        public TransportTransactionMode TransactionMode { get; } = TransportTransactionMode.ReceiveOnly;

        public override Task Shutdown(CancellationToken cancellationToken = default) => baseTransportInfrastructure.Shutdown(cancellationToken);

        public override string ToTransportAddress(QueueAddress address) => baseTransportInfrastructure.ToTransportAddress(address);

        readonly TransportInfrastructure baseTransportInfrastructure;
    }
}