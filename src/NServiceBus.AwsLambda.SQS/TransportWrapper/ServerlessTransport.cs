namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using Transport;

    sealed partial class ServerlessTransport : TransportDefinition
    {
        ServerlessTransportInfrastructure serverlessTransportInfrastructure;

        public ServerlessTransport(TransportDefinition baseTransport)
            : base(baseTransport.TransportTransactionMode, baseTransport.SupportsDelayedDelivery, baseTransport.SupportsPublishSubscribe, baseTransport.SupportsTTBR) =>
            BaseTransport = baseTransport;

        public TransportDefinition BaseTransport { get; }

        public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        {
            var baseTransportInfrastructure = await BaseTransport.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken)
                .ConfigureAwait(false);
            var errorQueueAddress = receivers.Length > 0
                ? baseTransportInfrastructure.ToTransportAddress(new QueueAddress(receivers[0].ErrorQueue)) // when using NSB, all receivers share the same error queue
                : null;
            serverlessTransportInfrastructure = new ServerlessTransportInfrastructure(baseTransportInfrastructure, errorQueueAddress);

            return serverlessTransportInfrastructure;
        }

        public ServerlessTransportInfrastructure GetTransportInfrastructure(IEndpointInstance _) =>
            // IEndpointInstance is only required to guarantee that GetTransportInfrastructure can't be called before NServiceBus called Initialize.
            serverlessTransportInfrastructure;

        public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() => supportedTransactionModes;

        readonly TransportTransactionMode[] supportedTransactionModes =
        {
            TransportTransactionMode.None,
            TransportTransactionMode.ReceiveOnly
        };
    }
}