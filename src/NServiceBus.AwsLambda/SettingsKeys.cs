namespace NServiceBus
{
    static class SettingsKeys
    {
        const string Prefix = "NServiceBus.AmazonSQS.";
        public const string S3BucketForLargeMessages = Prefix + nameof(S3BucketForLargeMessages);
    }
}