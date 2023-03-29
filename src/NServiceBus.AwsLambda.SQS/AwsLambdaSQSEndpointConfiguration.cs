namespace NServiceBus
{
    using System;
    using System.Threading.Tasks;
    using Amazon.SimpleNotificationService;
    using Amazon.SQS;
    using AwsLambda.SQS;
    using NServiceBus.AwsLambda.SQS.TransportWrapper;
    using NServiceBus.Logging;
    using Serialization;

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
            : this(endpointName, null, null)
        {

        }

        /// <summary>
        /// Creates a serverless NServiceBus endpoint running with an AmazonSQS SQS trigger.
        /// </summary>
        public AwsLambdaSQSEndpointConfiguration(string endpointName, IAmazonSQS sqsClient, IAmazonSimpleNotificationService snsClient)
        {
            EndpointConfiguration = new EndpointConfiguration(endpointName);

            LogManager.Use<LambdaLoggerDefinition>();

            recoverabilityPolicy.SendFailedMessagesToErrorQueue = true;

            // delayed delivery is disabled by default as the required FIFO queue might not exist
            EndpointConfiguration.Recoverability()
                .Delayed(c => c.NumberOfRetries(0))
                .CustomPolicy(recoverabilityPolicy.Invoke);

            if (sqsClient is null && snsClient is null)
            {
                // If both sqsClient/snsClient are null, use default constructor which will instantiate a default client instance.
                Transport = new SqsTransport();
            }
            else
            {
                // If either sqsClient or snsClient is null the transport will throw.
                Transport = new SqsTransport(sqsClient, snsClient);
            }

            RoutingSettings = EndpointConfiguration.UseTransport(Transport);

            // by default do not write custom diagnostics to file because lambda is readonly
            AdvancedConfiguration.CustomDiagnosticsWriter((_, _) => Task.CompletedTask);

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
        /// Amazon SQS transport routing settings
        /// </summary>
        public RoutingSettings<SqsTransport> RoutingSettings { get; }

        /// <summary>
        /// Amazon SQS Transport
        /// </summary>
        public SqsTransport Transport { get; }

        internal EndpointConfiguration EndpointConfiguration { get; }

        internal ServerlessTransport MakeServerless()
        {
            var serverlessTransport = new ServerlessTransport(Transport);
            EndpointConfiguration.UseTransport(serverlessTransport);

            return serverlessTransport;
        }

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