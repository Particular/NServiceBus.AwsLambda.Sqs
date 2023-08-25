namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System;
    using NServiceBus.Transport;

    sealed partial class ServerlessTransport
    {
#pragma warning disable CS0672 // Member overrides obsolete member

        [ObsoleteEx(Message = "Inject the ITransportAddressResolver type to access the address translation mechanism at runtime. See the NServiceBus version 8 upgrade guide for further details.",
                    TreatAsErrorFromVersion = "2",
                    RemoveInVersion = "3")]
        public override string ToTransportAddress(QueueAddress address) => throw new NotImplementedException();

#pragma warning restore CS0672 // Member overrides obsolete member
    }
}
