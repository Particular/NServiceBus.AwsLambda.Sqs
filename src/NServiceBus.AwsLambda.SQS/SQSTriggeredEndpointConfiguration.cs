namespace NServiceBus
{
    using Serverless;

    public class SQSTriggeredEndpointConfiguration : ServerlessEndpointConfiguration
    {
        public SQSTriggeredEndpointConfiguration(string endpointName) : base(endpointName)
        {
        }
    }
}