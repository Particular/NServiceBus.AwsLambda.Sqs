namespace NServiceBus.AwsLambda
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.SQSEvents;
    using Amazon.S3;

    static class MessageExtensions
    {
        public static async Task<byte[]> RetrieveBody(this TransportMessage transportMessage,
            IAmazonS3 s3Client,
            string s3BucketForLargeMessages,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(transportMessage.S3BodyKey))
            {
                return Convert.FromBase64String(transportMessage.Body);
            }

            var s3GetResponse = await s3Client.GetObjectAsync(s3BucketForLargeMessages,
                transportMessage.S3BodyKey,
                cancellationToken).ConfigureAwait(false);

            using (var memoryStream = new MemoryStream())
            {
                await s3GetResponse.ResponseStream.CopyToAsync(memoryStream).ConfigureAwait(false);
                return memoryStream.ToArray();
            }
        }

        public static DateTime GetAdjustedDateTimeFromServerSetAttributes(this SQSEvent.SQSMessage message, TimeSpan clockOffset)
        {
            var result = UnixEpoch.AddMilliseconds(long.Parse(message.Attributes["SentTimestamp"], NumberFormatInfo.InvariantInfo));
            // Adjust for clock skew between this endpoint and aws.
            // https://aws.amazon.com/blogs/developer/clock-skew-correction/
            return result + clockOffset;
        }

        static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}