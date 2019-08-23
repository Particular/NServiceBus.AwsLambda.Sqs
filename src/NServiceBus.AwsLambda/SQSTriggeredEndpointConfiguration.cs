using NServiceBus.Serverless;

namespace NServiceBus
{
    public class SQSTriggeredEndpointConfiguration : ServerlessEndpointConfiguration
    {
        public SQSTriggeredEndpointConfiguration(string endpointName) : base(endpointName)
        {
        }
    }
}
