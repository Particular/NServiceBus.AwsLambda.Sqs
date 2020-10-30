namespace NServiceBus.AwsLambda.Tests
{
    using NUnit.Framework;
    using Particular.Approvals;
    using PublicApiGenerator;

    [TestFixture]
    public class APIApprovals
    {
        [Test]
        public void Approve()
        {
            var options = new ApiGeneratorOptions
            {
                ExcludeAttributes = new[] {"System.Runtime.Versioning.TargetFrameworkAttribute"}
            };
            var publicApi = typeof(AwsLambdaSQSEndpoint).Assembly.GeneratePublicApi(options);
            Approver.Verify(publicApi);
        }
    }
}