#nullable enable
namespace NServiceBus.AwsLambda.SQS
{
    using System.Reflection;
    using Amazon.S3.Model;

    static class S3EncryptionMethodExtensions
    {
        public static void ModifyRequest(this S3EncryptionMethod encryptionMethod, GetObjectRequest request)
        {
            if (encryptionMethod == null)
            {
                return;
            }

            // TODO: Optimize
            var methodInfo = encryptionMethod.GetType().GetMethod("ModifyGetRequest", BindingFlags.NonPublic | BindingFlags.Instance);
            methodInfo!.Invoke(encryptionMethod, new object?[] { request });
        }
    }
}