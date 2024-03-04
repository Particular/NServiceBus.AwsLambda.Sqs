namespace NServiceBus.AwsLambda.SQS.Analyzer.Tests
{
    using System.Threading.Tasks;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using static AwsLambdaDiagnostics;

    [TestFixture]
    public class ConfigurationAnalyzerTestsCSharp8 : AnalyzerTestFixture<ConfigurationAnalyzer>
    {
        // HINT: In C# 7 this call is ambiguous with the LearningTransport version as the compiler cannot differentiate method calls via generic type constraints
        [TestCase("UseTransport<SqsTransport>()", UseTransportNotAllowedId)]
        public Task DiagnosticIsReportedForEndpointConfiguration(string configuration, string diagnosticId)
        {
            var source =
                $@"using NServiceBus;
using System;
using System.Threading.Tasks;
class Foo
{{
    void Bar(AwsLambdaSQSEndpointConfiguration endpointConfig)
    {{
        [|endpointConfig.AdvancedConfiguration.{configuration}|];

        var advancedConfig = endpointConfig.AdvancedConfiguration;
        [|advancedConfig.{configuration}|];
    }}
}}";

            return Assert(diagnosticId, source);
        }
        protected override LanguageVersion AnalyzerLanguageVersion => LanguageVersion.CSharp8;
    }
}