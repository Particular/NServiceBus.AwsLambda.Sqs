namespace NServiceBus.AwsLambda.SQS
{
    using System;
    using System.IO;
    using Amazon.S3;
    using System.Threading.Tasks;
    using System.Threading;
    using System.Linq;
    using System.Text;

    static class TransportMessageExtensions
    {
        public static async Task<byte[]> RetrieveBody(this TransportMessage transportMessage,
            IAmazonS3 s3Client,
            string s3BucketForLargeMessages,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(transportMessage.S3BodyKey))
            {
                if (transportMessage.Body == TransportMessage.EmptyMessage)
                {
                    return EmptyMessage;
                }

                return ConvertBody(transportMessage.Body, transportMessage.Headers.Keys.Contains(TransportHeaders.Headers));
            }

            if (s3Client == null)
            {
                throw new Exception("Unable to retrieve the body from S3. Configure the bucket name and key prefix with `transport.S3(string bucketForLargeMessages, string keyPrefix)`");
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

        static byte[] ConvertBody(string body, bool isNativeMessage)
        {
            var encoding = Encoding.UTF8;

            if (isNativeMessage)
            {
                return encoding.GetBytes(body);
            }
            else
            {
                try
                {
                    return Convert.FromBase64String(body);
                }
                catch (FormatException)
                {
                    return encoding.GetBytes(body);
                }
            }
        }

        static readonly byte[] EmptyMessage = Array.Empty<byte>();

    }
}
