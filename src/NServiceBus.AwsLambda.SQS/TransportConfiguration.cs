namespace NServiceBus.AwsLambda
{
    using Settings;

    class TransportConfiguration
    {
        public TransportConfiguration(ReadOnlySettings settings)
        {
            // Accessing the settings bag during runtime means a lot of boxing and unboxing,
            // all properties of this class are lazy initialized once they are accessed
            this.settings = settings;
        }

        public string S3BucketForLargeMessages
        {
            get
            {
                if (s3BucketForLargeMessages == null)
                {
                    s3BucketForLargeMessages = settings.GetOrDefault<string>(SettingsKeys.S3BucketForLargeMessages);
                }

                return s3BucketForLargeMessages;
            }
        }

        ReadOnlySettings settings;
        string s3BucketForLargeMessages;
    }
}