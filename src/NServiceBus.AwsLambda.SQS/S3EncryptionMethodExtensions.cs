#nullable enable
namespace NServiceBus.AwsLambda.SQS
{
    using System.Runtime.CompilerServices;
    using Amazon.S3.Model;

    static class S3EncryptionMethodExtensions
    {
        public static void ModifyRequest(this S3EncryptionMethod encryptionMethod, GetObjectRequest request)
        {
            if (encryptionMethod == null)
            {
                return;
            }

            ModifyGetRequest(encryptionMethod, request);
        }

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ModifyGetRequest")]
        static extern void ModifyGetRequest(S3EncryptionMethod encryptionMethod, GetObjectRequest get);
    }
}