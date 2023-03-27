namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using Transport;

    class ServerlessTransport : TransportDefinition
    {
        // HINT: This constant is defined in NServiceBus but is not exposed
        const string MainReceiverId = "Main";

        public ServerlessTransport(TransportDefinition baseTransport)
            : base(baseTransport.TransportTransactionMode, baseTransport.SupportsDelayedDelivery, baseTransport.SupportsPublishSubscribe, baseTransport.SupportsTTBR)
        {
            BaseTransport = baseTransport;
        }

        public TransportDefinition BaseTransport { get; }

        public PipelineInvoker PipelineInvoker { get; private set; }

        public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        {
            //   hostSettings.
            var baseTransportInfrastructure = await BaseTransport.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken).ConfigureAwait(false);
            var serverlessTransportInfrastructure = new ServerlessTransportInfrastructure(baseTransportInfrastructure);
            PipelineInvoker = (PipelineInvoker)serverlessTransportInfrastructure.Receivers[MainReceiverId];

            return serverlessTransportInfrastructure;

        }

#pragma warning disable CS0672 // Member overrides obsolete member
#pragma warning disable CS0618 // Type or memeber is obsolete

        public override string ToTransportAddress(QueueAddress address) => BaseTransport.ToTransportAddress(address);

#pragma warning restore CS0618 // Type or memeber is obsolete
#pragma warning restore CS0672 // Member overrides obsolete member


        public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() => supportedTransactionModes;

        readonly TransportTransactionMode[] supportedTransactionModes =
        {
            TransportTransactionMode.None,
            TransportTransactionMode.ReceiveOnly
        };
    }
}