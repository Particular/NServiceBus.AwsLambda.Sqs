namespace NServiceBus.AwsLambda.SQS.Analyzer.Tests
{
    using System.Threading.Tasks;
    using NUnit.Framework;
    using static AwsLambdaDiagnostics;

    [TestFixture]
    public class TransportConfigurationAnalyzerTests : AnalyzerTestFixture<ConfigurationAnalyzer>
    {
        [TestCase("TransportTransactionMode", "TransportTransactionMode.None", TransportTransactionModeNotAllowedId)]
        public Task DiagnosticIsReportedTransportConfigurationDirect(string configName, string configValue, string diagnosticId)
        {
            var source =
                $@"using NServiceBus;
using System;
using System.Threading.Tasks;
class Foo
{{
    void Direct(AwsLambdaSQSEndpointConfiguration endpointConfig)
    {{
        [|endpointConfig.Transport.{configName}|] = {configValue};

        var transportConfig = endpointConfig.Transport;
        [|transportConfig.{configName}|] = {configValue};
    }}
}}";

            return Assert(diagnosticId, source);
        }

        [TestCase("Transactions", "TransportTransactionMode.None", TransportTransactionModeNotAllowedId)]
        public Task DiagnosticIsReportedTransportConfigurationExtension(string configName, string configValue, string diagnosticId)
        {
            var source =
                $@"using NServiceBus;
using System;
using System.Threading.Tasks;
class Foo
{{
    void Extension(TransportExtensions<SqsTransport> transportExtension)
    {{
        [|transportExtension.{configName}({configValue})|];
    }}
}}";

            return Assert(diagnosticId, source);
        }
    }
}