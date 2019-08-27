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

        internal string S3BucketForLargeMessages { get; set; }

        /// <summary>
        /// Amazon SQS transport
        /// </summary>
        public TransportExtensions<SqsTransport> Transport { get; }

        /// <summary>
        /// Configures the S3 Bucket that will be used to store message bodies
        /// for messages that are larger than 256k in size. If this option is not specified,
        /// S3 will not be used at all. Any attempt to send a message larger than 256k will
        /// throw if this option hasn't been specified. If the specified bucket doesn't
        /// exist, NServiceBus.AmazonSQS will create it when the endpoint starts up.
        /// Allows to optionally configure the client factory.
        /// </summary>
        /// <param name="bucketForLargeMessages">The name of the S3 Bucket.</param>
        /// <param name="keyPrefix">The path within the specified S3 Bucket to store large message bodies.</param>
        public void S3(string bucketForLargeMessages, string keyPrefix)
        {
            Transport.S3(bucketForLargeMessages, keyPrefix);
            S3BucketForLargeMessages = bucketForLargeMessages;
        }
    }
}