namespace NServiceBus
{
    static class SettingsKeys
    {
        const string Prefix = "NServiceBus.AmazonSQS.";
        public const string SqsClientFactory = Prefix + nameof(SqsClientFactory);
        public const string S3ClientFactory = Prefix + nameof(S3ClientFactory);
        public const string S3BucketForLargeMessages = Prefix + nameof(S3BucketForLargeMessages);
    }
}