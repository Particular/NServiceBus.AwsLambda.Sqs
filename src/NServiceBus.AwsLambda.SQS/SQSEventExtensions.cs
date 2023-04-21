namespace NServiceBus.AwsLambda.SQS
{
    using System.Collections.Generic;
    using System.IO;
    using Amazon.SQS.Model;
    using static Amazon.Lambda.SQSEvents.SQSEvent;

    static class SQSEventExtensions
    {
        public static Message ToMessage(this SQSMessage source)
        {
            var target = new Message();

            target.MessageId = source.MessageId ?? target.MessageId;
            target.ReceiptHandle = source.ReceiptHandle ?? target.ReceiptHandle;
            target.Attributes = source.Attributes ?? target.Attributes;
            target.Body = source.Body ?? target.Body;
            target.MD5OfBody = source.Md5OfBody ?? target.MD5OfBody;
            target.MD5OfMessageAttributes = source.Md5OfMessageAttributes ?? target.MD5OfMessageAttributes;
            target.MessageAttributes = ToMessageAttributes(source.MessageAttributes) ?? target.MessageAttributes;

            return target;
        }

        static Dictionary<string, MessageAttributeValue> ToMessageAttributes(Dictionary<string, MessageAttribute> messageAttributes)
        {
            var newMessageAttributes = new Dictionary<string, MessageAttributeValue>(messageAttributes.Count);

            foreach (var attribute in messageAttributes)
            {
                newMessageAttributes.Add(attribute.Key, CopyMessageAttributeValue(attribute.Value));
            }

            return newMessageAttributes;
        }

        static MessageAttributeValue CopyMessageAttributeValue(MessageAttribute source)
        {
            var target = new MessageAttributeValue();

            target.DataType = source.DataType ?? target.DataType;
            target.StringValue = source.StringValue ?? target.StringValue;
            target.BinaryValue = source.BinaryValue ?? target.BinaryValue;

            // The SQS client returns empty lists instead of null
            target.StringListValues = source.StringListValues ?? new List<string>(0);
            target.BinaryListValues = source.BinaryListValues ?? new List<MemoryStream>(0);

            return target;
        }
    }
}
