namespace NServiceBus.AwsLambda.SQS
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Amazon.SQS.Model;
    using static Amazon.Lambda.SQSEvents.SQSEvent;

    static class SQSEventExtensions
    {
        public static Message ToMessage(this SQSMessage source)
        {
            var target = new Message();

            target.SetIfNotNull(source.MessageId, static (t, value) => t.MessageId = value);
            target.SetIfNotNull(source.ReceiptHandle, static (t, value) => t.ReceiptHandle = value);
            target.SetIfNotNull(source.Attributes, static (t, value) => t.Attributes = value);
            target.SetIfNotNull(source.Body, static (t, value) => t.Body = value);
            target.SetIfNotNull(source.Md5OfBody, static (t, value) => t.MD5OfBody = value);
            target.SetIfNotNull(source.Md5OfMessageAttributes, static (t, value) => t.MD5OfMessageAttributes = value);
            target.SetIfNotNull(source.MessageAttributes, static (t, value) => t.MessageAttributes = ToMessageAttributes(value));

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

            target.SetIfNotNull(source.DataType, static (t, value) => t.DataType = value);
            target.SetIfNotNull(source.StringValue, static (t, value) => t.StringValue = value);
            target.SetIfNotNull(source.BinaryValue, static (t, value) => t.BinaryValue = value);

            // The SQS client returns empty lists instead of null
            target.StringListValues = source.StringListValues ?? new List<string>(0);
            target.BinaryListValues = source.BinaryListValues ?? new List<MemoryStream>(0);

            return target;
        }

        static void SetIfNotNull<T>(this Message message, T value, Action<Message, T> doIfNotNull)
        {
            if (value is not null)
            {
                doIfNotNull(message, value);
            }
        }

        static void SetIfNotNull<T>(this MessageAttributeValue messageAttribute, T value, Action<MessageAttributeValue, T> doIfNotNull)
        {
            if (value is not null)
            {
                doIfNotNull(messageAttribute, value);
            }
        }
    }
}
