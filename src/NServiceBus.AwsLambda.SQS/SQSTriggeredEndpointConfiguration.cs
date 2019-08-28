namespace NServiceBus
{
    using Serverless;

    /// <summary>
    /// Represents a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
    /// </summary>
    public class SQSTriggeredEndpointConfiguration : ServerlessEndpointConfiguration
    {
        /// <summary>
        /// Creates a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
        /// </summary>
        /// <param name="endpointName">The endpoint name to be used.</param>
        public SQSTriggeredEndpointConfiguration(string endpointName) : base(endpointName)
        {
            Transport = UseTransport<SqsTransport>();
        }

        /// <summary>
        /// Amazon SQS transport
        /// </summary>
        public TransportExtensions<SqsTransport> Transport { get; }
    }
}