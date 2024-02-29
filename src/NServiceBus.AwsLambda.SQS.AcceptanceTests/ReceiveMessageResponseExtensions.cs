namespace NServiceBus.AcceptanceTests
{
    using Amazon.Lambda.SQSEvents;
    using Amazon.SQS.Model;

    static class ReceiveMessageResponseExtensions
    {
        public static SQSEvent ToSQSEvent(this ReceiveMessageResponse response)
        {
            var @event = new SQSEvent
            {
                Records = []
            };
            foreach (var message in response.Messages)
            {
                @event.Records.Add(message.ToSQSMessage());
            }

            return @event;
        }
    }
}