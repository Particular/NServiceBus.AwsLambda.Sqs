namespace NServiceBus.AwsLambda.SQS
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Amazon.Lambda.SQSEvents;
    using Amazon.SQS.Model;

    static class MessageTypeAdapter
    {
        public static Message ToMessage(SQSEvent.SQSMessage sqsEventRecord)
        {
            return new Message()
            {
                MessageId = sqsEventRecord.MessageId,
                ReceiptHandle = sqsEventRecord.ReceiptHandle,
                Attributes = sqsEventRecord.Attributes,
                Body = sqsEventRecord.Body,
                MD5OfBody = sqsEventRecord.Md5OfBody,
                MD5OfMessageAttributes = sqsEventRecord.Md5OfMessageAttributes,
                MessageAttributes = ToMessageAttributes(sqsEventRecord.MessageAttributes)
            };
        }

        static Dictionary<string, MessageAttributeValue> ToMessageAttributes(Dictionary<string, SQSEvent.MessageAttribute> messageAttributes)
        {
            return new Dictionary<string, MessageAttributeValue>(messageAttributes
            .Select(s => new KeyValuePair<string, MessageAttributeValue>(s.Key, new MessageAttributeValue()
            {
                DataType = s.Value.DataType,
                BinaryValue = s.Value.BinaryValue,
                StringValue = s.Value.StringValue,
                // The SQS client returns empty lists instead of null
                StringListValues = s.Value.StringListValues ?? new List<string>(),
                BinaryListValues = s.Value.BinaryListValues ?? new List<MemoryStream>(),
            })));
        }
    }
}
