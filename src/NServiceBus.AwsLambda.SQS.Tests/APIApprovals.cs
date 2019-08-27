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
            var publicApi = ApiGenerator.GeneratePublicApi(typeof(AwsLambdaSQSEndpoint).Assembly, excludeAttributes: new[] {"System.Runtime.Versioning.TargetFrameworkAttribute"});
            Approver.Verify(publicApi);
        }
    }
}