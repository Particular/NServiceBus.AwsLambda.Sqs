namespace NServiceBus
{
    using System.Threading.Tasks;
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
            
            // by default do not write custom diagnostics to file because lambda is readonly
            AdvancedConfiguration.CustomDiagnosticsWriter(diagnostics => Task.CompletedTask);
        }

        /// <summary>
        /// Amazon SQS transport
        /// </summary>
        public TransportExtensions<SqsTransport> Transport { get; }
    }
}