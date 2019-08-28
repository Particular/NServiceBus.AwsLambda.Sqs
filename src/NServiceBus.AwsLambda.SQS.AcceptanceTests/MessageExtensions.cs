namespace NServiceBus.AwsLambda.SQS.AcceptanceTests
{
    using System.Linq;
    using Amazon.Lambda.SQSEvents;
    using Amazon.SQS.Model;

    static class MessageExtensions
    {
        public static SQSEvent.SQSMessage ToSQSMessage(this Message message)
        {
            var sqsMessage = new SQSEvent.SQSMessage
            {
                Attributes = message.Attributes,
                Body = message.Body,
                Md5OfBody = message.MD5OfBody,
                Md5OfMessageAttributes = message.MD5OfMessageAttributes,
                // not everything taken care of yet
                MessageAttributes = message.MessageAttributes.ToDictionary(x => x.Key, x => new SQSEvent.MessageAttribute { DataType = x.Value.DataType, StringValue = x.Value.StringValue}),
                MessageId = message.MessageId,
                ReceiptHandle = message.ReceiptHandle,
            };
            return sqsMessage;
        }
    }
}