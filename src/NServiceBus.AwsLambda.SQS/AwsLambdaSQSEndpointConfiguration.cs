namespace NServiceBus
{
    using System;
    using System.Threading.Tasks;
    using AwsLambda.SQS;
    using AwsLambda.SQS.TransportWrapper;
    using Configuration.AdvancedExtensibility;
    using NServiceBus.Logging;
    using Serialization;

    /// <summary>
    /// Represents a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
    /// </summary>
    public class AwsLambdaSQSEndpointConfiguration
    {
        readonly ServerlessRecoverabilityPolicy recoverabilityPolicy = new ServerlessRecoverabilityPolicy();
        ServerlessTransport serverlessTransport;

        static AwsLambdaSQSEndpointConfiguration()
        {
            LogManager.Use<LambdaLoggerDefinition>();
        }

        /// <summary>
        /// Creates a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
        /// </summary>
        /// <param name="endpointName">The endpoint name to be used.</param>
        /// <param name="transport">The configured SQS transport</param>
        public AwsLambdaSQSEndpointConfiguration(string endpointName, SqsTransport transport)
        {
            EndpointConfiguration = new EndpointConfiguration(endpointName);

            //make sure a call to "onError" will move the message to the error queue.
            EndpointConfiguration.Recoverability().Delayed(c => c.NumberOfRetries(0));
            // send failed messages to the error queue
            recoverabilityPolicy.SendFailedMessagesToErrorQueue = true;
            EndpointConfiguration.Recoverability().CustomPolicy(recoverabilityPolicy.Invoke);

            Transport = transport;

            serverlessTransport = new ServerlessTransport(Transport);
            var serverlessRouting = AdvancedConfiguration.UseTransport(serverlessTransport);
            Routing = new RoutingSettings<SqsTransport>(serverlessRouting.GetSettings());

            // by default do not write custom diagnostics to file because lambda is readonly
            AdvancedConfiguration.CustomDiagnosticsWriter((diagnostics, token) => Task.CompletedTask);

            TrySpecifyDefaultLicense();
        }

        void TrySpecifyDefaultLicense()
        {
            var licenseText = Environment.GetEnvironmentVariable("NSERVICEBUS_LICENSE");
            if (!string.IsNullOrWhiteSpace(licenseText))
            {
                EndpointConfiguration.License(licenseText);
            }
        }

        /// <summary>
        /// Amazon SQS transport
        /// </summary>
        public SqsTransport Transport { get; }

        /// <summary>
        /// The routing configuration.
        /// </summary>
        public RoutingSettings<SqsTransport> Routing { get; }

        internal EndpointConfiguration EndpointConfiguration { get; }
        internal PipelineInvoker PipelineInvoker => serverlessTransport.PipelineInvoker;

        /// <summary>
        /// Gives access to the underlying endpoint configuration for advanced configuration options.
        /// </summary>
        public EndpointConfiguration AdvancedConfiguration => EndpointConfiguration;

        /// <summary>
        /// Define the serializer to be used.
        /// </summary>
        public SerializationExtensions<T> UseSerialization<T>() where T : SerializationDefinition, new()
        {
            return EndpointConfiguration.UseSerialization<T>();
        }

        /// <summary>
        /// Disables moving messages to the error queue even if an error queue name is configured.
        /// </summary>
        public void DoNotSendMessagesToErrorQueue()
        {
            recoverabilityPolicy.SendFailedMessagesToErrorQueue = false;
        }
    }
}