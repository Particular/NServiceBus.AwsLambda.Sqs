namespace NServiceBus
{
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Core;
    using Amazon.Lambda.SQSEvents;

    /// <summary>
    /// An NServiceBus endpoint hosted in AWS Lambda which does not receive messages automatically but only handles
    /// messages explicitly passed to it by the caller.
    /// </summary>
    public interface IAwsLambdaSQSEndpoint
    {
        /// <summary>
        /// Processes a messages received from an SQS trigger using the NServiceBus message pipeline.
        /// </summary>
        Task Process(SQSEvent @event, ILambdaContext lambdaContext, CancellationToken cancellationToken = default);
    }
}