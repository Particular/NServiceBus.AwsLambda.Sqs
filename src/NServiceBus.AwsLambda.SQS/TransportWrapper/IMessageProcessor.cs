namespace NServiceBus.AwsLambda.SQS.TransportWrapper
{
    using System.Threading.Tasks;
    using Transport;

    interface IMessageProcessor
    {
        Task<ErrorHandleResult> PushFailedMessage(ErrorContext errorContext);
        Task PushMessage(MessageContext messageContext);
    }
}