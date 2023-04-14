namespace NServiceBus.AwsLambda.SQS
{
    using System.Collections.Generic;
    using System.IO;
    using Amazon.SQS.Model;
    using static Amazon.Lambda.SQSEvents.SQSEvent;

    static class SQSEventExtensions
    {
        public static Message ToMessage(this SQSMessage sqsEventRecord)
        {
            var newMessage = new Message();

            if (!string.IsNullOrEmpty(sqsEventRecord.MessageId))
            {
                newMessage.MessageId = sqsEventRecord.MessageId;
            }

            if (!string.IsNullOrEmpty(sqsEventRecord.ReceiptHandle))
            {
                newMessage.ReceiptHandle = sqsEventRecord.ReceiptHandle;
            }

            if (sqsEventRecord.Attributes != null)
            {
                newMessage.Attributes = sqsEventRecord.Attributes;
            }

            if (!string.IsNullOrEmpty(sqsEventRecord.Body))
            {
                newMessage.Body = sqsEventRecord.Body;
            }

            if (!string.IsNullOrEmpty(sqsEventRecord.Md5OfBody))
            {
                newMessage.MD5OfBody = sqsEventRecord.Md5OfBody;
            }

            if (!string.IsNullOrEmpty(sqsEventRecord.Md5OfMessageAttributes))
            {
                newMessage.MD5OfMessageAttributes = sqsEventRecord.Md5OfMessageAttributes;
            }

            if (!string.IsNullOrEmpty(sqsEventRecord.Md5OfMessageAttributes))
            {
                newMessage.MD5OfMessageAttributes = sqsEventRecord.Md5OfMessageAttributes;
            }

            if (sqsEventRecord.MessageAttributes != null)
            {
                newMessage.MessageAttributes = ToMessageAttributes(sqsEventRecord.MessageAttributes);
            }

            return newMessage;
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

        static MessageAttributeValue CopyMessageAttributeValue(MessageAttribute valueToCopy)
        {
            var newValue = new MessageAttributeValue();

            if (!string.IsNullOrEmpty(valueToCopy.DataType))
            {
                newValue.DataType = valueToCopy.DataType;
            }

            if (!string.IsNullOrEmpty(valueToCopy.StringValue))
            {
                newValue.StringValue = valueToCopy.StringValue;
            }

            if (valueToCopy.BinaryValue != null)
            {
                newValue.BinaryValue = valueToCopy.BinaryValue;
            }

            // The SQS client returns empty lists instead of null
            newValue.StringListValues = valueToCopy.StringListValues ?? new List<string>(0);
            newValue.BinaryListValues = valueToCopy.BinaryListValues ?? new List<MemoryStream>(0);

            return newValue;
        }
    }
}
