namespace NServiceBus
{
    using NServiceBus.AmazonSQS;
    using Serverless;

    public class SQSTriggeredEndpointConfiguration : ServerlessEndpointConfiguration
    {
        private readonly TransportExtensions<SqsTransport> transport;

        internal SQSTransportConfiguration TransportConfiguration { get; }

        public SQSTriggeredEndpointConfiguration(string endpointName) : base(endpointName)
        {
            transport = UseTransport<SqsTransport>();
        }

        internal string S3BucketForLargeMessages { get; set; }

        public void S3(string bucketForLargeMessages, string keyPrefix)
        {
            S3BucketForLargeMessages = bucketForLargeMessages;

            transport.S3(bucketForLargeMessages, keyPrefix);
        }
    }
}