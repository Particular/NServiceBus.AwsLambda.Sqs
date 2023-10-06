namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    sealed class ServerlessTransportInfrastructure : TransportInfrastructure
    {
        // HINT: This constant is defined in NServiceBus but is not exposed
        const string MainReceiverId = "Main";

        public IMessageProcessor PipelineInvoker { get; private set; }

        public string ErrorQueueAddress { get; set; }

        public bool IsSendOnly { get; private set; }

        public ServerlessTransportInfrastructure(TransportInfrastructure baseTransportInfrastructure, string errorQueueAddress)
        {
            this.baseTransportInfrastructure = baseTransportInfrastructure;
            Dispatcher = baseTransportInfrastructure.Dispatcher;
            Receivers = baseTransportInfrastructure.Receivers.ToDictionary(r => r.Key, r => (IMessageReceiver)new PipelineInvoker(r.Value));

            IsSendOnly = Receivers.Count == 0;
            PipelineInvoker = IsSendOnly
                ? new SendOnlyMessageProcessor()
                : (PipelineInvoker)Receivers[MainReceiverId];

            ErrorQueueAddress = errorQueueAddress;
        }

        public override Task Shutdown(CancellationToken cancellationToken = default) => baseTransportInfrastructure.Shutdown(cancellationToken);

        public override string ToTransportAddress(QueueAddress address) => baseTransportInfrastructure.ToTransportAddress(address);

        readonly TransportInfrastructure baseTransportInfrastructure;
    }
}