namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using Transport;

    sealed class ServerlessTransport : TransportDefinition
    {
        ServerlessTransportInfrastructure serverlessTransportInfrastructure;

        public ServerlessTransport(TransportDefinition baseTransport)
            : base(baseTransport.TransportTransactionMode, baseTransport.SupportsDelayedDelivery, baseTransport.SupportsPublishSubscribe, baseTransport.SupportsTTBR) =>
            BaseTransport = baseTransport;

        public TransportDefinition BaseTransport { get; }

        public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
        {
            if (receivers.Length == 0)
            {
                throw new Exception(
                    "SendOnly endpoints are not supported in this version. Upgrade to a newer version of the NServiceBus.AwsLambda.SQS package to use SendOnly endpoints");
            }

            var baseTransportInfrastructure = await BaseTransport.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken)
                .ConfigureAwait(false);
            var errorQueueAddress = baseTransportInfrastructure.ToTransportAddress(new QueueAddress(receivers[0].ErrorQueue));

            serverlessTransportInfrastructure = new ServerlessTransportInfrastructure(baseTransportInfrastructure, errorQueueAddress);

            return serverlessTransportInfrastructure;

        }

        public ServerlessTransportInfrastructure GetTransportInfrastructure(IEndpointInstance _) =>
            // IEndpointInstance is only required to guarantee that GetTransportInfrastructure can't be called before NServiceBus called Initialize.
            serverlessTransportInfrastructure;

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