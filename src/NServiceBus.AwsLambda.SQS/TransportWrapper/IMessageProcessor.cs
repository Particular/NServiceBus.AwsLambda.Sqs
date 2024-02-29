namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Threading;
    using System.Threading.Tasks;
    using Transport;

    interface IMessageProcessor
    {
        string ReceiveAddress { get; }
        Task<ErrorHandleResult> PushFailedMessage(ErrorContext errorContext, CancellationToken cancellationToken = default);
        Task PushMessage(MessageContext messageContext, CancellationToken cancellationToken = default);
    }
}