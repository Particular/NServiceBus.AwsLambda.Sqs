#nullable enable

namespace NServiceBus.AwsLambda.SQS
{
    using System;
    using System.Globalization;
    using Amazon.Lambda.SQSEvents;

    static class MessageExtensions
    {
        public static DateTimeOffset GetAdjustedDateTimeFromServerSetAttributes(this SQSEvent.SQSMessage message, string attributeName, TimeSpan clockOffset)
        {
            var result = UnixEpoch.AddMilliseconds(long.Parse(message.Attributes[attributeName], NumberFormatInfo.InvariantInfo));
            // Adjust for clock skew between this endpoint and aws.
            // https://aws.amazon.com/blogs/developer/clock-skew-correction/
            return result + clockOffset;
        }

        static readonly DateTimeOffset UnixEpoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}