namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using Transport;

    sealed class ServerlessTransport : TransportDefinition
    {
        // HINT: This constant is defined in NServiceBus but is not exposed
        const string MainReceiverId = "Main";
        const string SendOnlyConfigKey = "Endpoint.SendOnly";


        public ServerlessTransport(TransportDefinition baseTransport)
            : base(baseTransport.TransportTransactionMode, baseTransport.SupportsDelayedDelivery, baseTransport.SupportsPublishSubscribe, baseTransport.SupportsTTBR) =>
            BaseTransport = baseTransport;

        public TransportDefinition BaseTransport { get; }

        public IMessageProcessor PipelineInvoker { get; private set; }

        public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        {
            var baseTransportInfrastructure = await BaseTransport.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken)
                .ConfigureAwait(false);

            var serverlessTransportInfrastructure = new ServerlessTransportInfrastructure(baseTransportInfrastructure);

            var isSendOnly = hostSettings.CoreSettings.GetOrDefault<bool>(SendOnlyConfigKey);

            PipelineInvoker = isSendOnly
                ? new SendOnlyMessageProcessor()
                : (PipelineInvoker)serverlessTransportInfrastructure.Receivers[MainReceiverId];

            return serverlessTransportInfrastructure;
        }

#pragma warning disable CS0672 // Member overrides obsolete member
#pragma warning disable CS0618 // Type or member is obsolete

        public override string ToTransportAddress(QueueAddress address) => BaseTransport.ToTransportAddress(address);

#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0672 // Member overrides obsolete member


        public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() => supportedTransactionModes;

        readonly TransportTransactionMode[] supportedTransactionModes =
        {
            TransportTransactionMode.None,
            TransportTransactionMode.ReceiveOnly
        };
    }
}