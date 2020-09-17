namespace NServiceBus
{
    using System;
    using System.Threading.Tasks;
    using AwsLambda.SQS;
    using AwsLambda.SQS.TransportWrapper;
    using Configuration.AdvancedExtensibility;
    using Serialization;
    using Transport;

    /// <summary>
    /// Represents a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
    /// </summary>
    public class AwsLambdaSQSEndpointConfiguration
    {
        readonly ServerlessRecoverabilityPolicy recoverabilityPolicy = new ServerlessRecoverabilityPolicy();

        /// <summary>
        /// Creates a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
        /// </summary>
        /// <param name="endpointName">The endpoint name to be used.</param>
        public AwsLambdaSQSEndpointConfiguration(string endpointName)
        {
            EndpointConfiguration = new EndpointConfiguration(endpointName);

            EndpointConfiguration.UsePersistence<InMemoryPersistence>();

            //make sure a call to "onError" will move the message to the error queue.
            EndpointConfiguration.Recoverability().Delayed(c => c.NumberOfRetries(0));
            // send failed messages to the error queue
            recoverabilityPolicy.SendFailedMessagesToErrorQueue = true;
            EndpointConfiguration.Recoverability().CustomPolicy(recoverabilityPolicy.Invoke);

            Transport = UseTransport<SqsTransport>();

            // by default do not write custom diagnostics to file because lambda is readonly
            AdvancedConfiguration.CustomDiagnosticsWriter(diagnostics => Task.CompletedTask);

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
        public TransportExtensions<SqsTransport> Transport { get; }

        internal EndpointConfiguration EndpointConfiguration { get; }
        internal PipelineInvoker PipelineInvoker { get; private set; }

        /// <summary>
        /// Gives access to the underlying endpoint configuration for advanced configuration options.
        /// </summary>
        public EndpointConfiguration AdvancedConfiguration => EndpointConfiguration;

        /// <summary>
        /// Define a transport to be used when sending and publishing messages.
        /// </summary>
        protected TransportExtensions<TTransport> UseTransport<TTransport>()
            where TTransport : TransportDefinition, new()
        {
            var serverlessTransport = EndpointConfiguration.UseTransport<ServerlessTransport<TTransport>>();
            //TODO improve
            PipelineInvoker = PipelineAccess(serverlessTransport);
            return BaseTransportConfiguration(serverlessTransport);
        }

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

        static PipelineInvoker PipelineAccess<TBaseTransport>(
            TransportExtensions<ServerlessTransport<TBaseTransport>> transportConfiguration) where TBaseTransport : TransportDefinition, new()
        {
            return transportConfiguration.GetSettings().GetOrCreate<PipelineInvoker>();
        }

        static TransportExtensions<TBaseTransport> BaseTransportConfiguration<TBaseTransport>(
            TransportExtensions<ServerlessTransport<TBaseTransport>> transportConfiguration) where TBaseTransport : TransportDefinition, new()
        {
            return new TransportExtensions<TBaseTransport>(transportConfiguration.GetSettings());
        }
    }
}