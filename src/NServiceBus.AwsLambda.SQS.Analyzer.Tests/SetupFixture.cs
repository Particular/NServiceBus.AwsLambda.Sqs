namespace NServiceBus.AwsLambda.SQS.Analyzer.Tests;

using Microsoft.CodeAnalysis;
using NUnit.Framework;
using Particular.AnalyzerTesting;

[SetUpFixture]
public class SetupFixture
{
    [OneTimeSetUp]
    public void Setup()
    {
        AnalyzerTest.ConfigureAllAnalyzerTests(test => test.AddReferences(References));
    }

    public static MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(EndpointConfiguration).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(SqsTransport).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(AwsLambdaSQSEndpointConfiguration).Assembly.Location),
    ];
}