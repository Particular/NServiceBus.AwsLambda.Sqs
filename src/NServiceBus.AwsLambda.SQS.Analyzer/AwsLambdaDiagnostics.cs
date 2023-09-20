namespace NServiceBus.AwsLambda.SQS.Analyzer
{
    using Microsoft.CodeAnalysis;

    public static class AwsLambdaDiagnostics
    {
        public const string PurgeOnStartupNotAllowedId = "NSBLAM001";
        public const string LimitMessageProcessingToNotAllowedId = "NSBLAM002";
        public const string DefineCriticalErrorActionNotAllowedId = "NSBLAM003";
        public const string SetDiagnosticsPathNotAllowedId = "NSBLAM004";
        public const string MakeInstanceUniquelyAddressableNotAllowedId = "NSBLAM005";
        public const string UseTransportNotAllowedId = "NSBLAM006";
        public const string OverrideLocalAddressNotAllowedId = "NSBLAM007";
        public const string RouteReplyToThisInstanceNotAllowedId = "NSBLAM008";
        public const string RouteToThisInstanceNotAllowedId = "NSBLAM009";
        public const string TransportTransactionModeNotAllowedId = "NSBLAM010";

        const string DiagnosticCategory = "NServiceBus.AwsLambda";

        internal static readonly DiagnosticDescriptor PurgeOnStartupNotAllowed = new DiagnosticDescriptor(
             id: PurgeOnStartupNotAllowedId,
             title: "PurgeOnStartup is not supported in AWS Lambda",
             messageFormat: "AWS Lambda endpoints do not support PurgeOnStartup.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor LimitMessageProcessingToNotAllowed = new DiagnosticDescriptor(
             id: LimitMessageProcessingToNotAllowedId,
             title: "LimitMessageProcessing is not supported in AWS Lambda",
             messageFormat: "Concurrency-related settings can be configured with the Lambda API.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor DefineCriticalErrorActionNotAllowed = new DiagnosticDescriptor(
             id: DefineCriticalErrorActionNotAllowedId,
             title: "DefineCriticalErrorAction is not supported in AWS Lambda",
             messageFormat: "AWS Lambda endpoints do not control the application lifecycle and should not define behavior in the case of critical errors.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor SetDiagnosticsPathNotAllowed = new DiagnosticDescriptor(
             id: SetDiagnosticsPathNotAllowedId,
             title: "SetDiagnosticsPath is not supported in AWS Lambda",
             messageFormat: "AWS Lambda endpoints should not write diagnostics to the local file system. Use CustomDiagnosticsWriter to write diagnostics to another location.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor MakeInstanceUniquelyAddressableNotAllowed = new DiagnosticDescriptor(
             id: MakeInstanceUniquelyAddressableNotAllowedId,
             title: "MakeInstanceUniquelyAddressable is not supported in AWS Lambda",
             messageFormat: "AWS Lambda endpoints have unpredictable lifecycles and should not be uniquely addressable.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor UseTransportNotAllowed = new DiagnosticDescriptor(
             id: UseTransportNotAllowedId,
             title: "UseTransport is not supported in AWS Lambda",
             messageFormat: "The package configures Amazon SQS transport by default. Use AwsLambdaSQSEndpointConfiguration.Transport to access the transport configuration.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Warning,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor OverrideLocalAddressNotAllowed = new DiagnosticDescriptor(
             id: OverrideLocalAddressNotAllowedId,
             title: "OverrideLocalAddress is not supported in AWS Lambda",
             messageFormat: "The NServiceBus endpoint address in AWS Lambda is determined by the serverless template.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor RouteReplyToThisInstanceNotAllowed = new DiagnosticDescriptor(
             id: RouteReplyToThisInstanceNotAllowedId,
             title: "RouteReplyToThisInstance is not supported in AWS Lambda",
             messageFormat: "AWS Lambda instances cannot be directly addressed as they have a highly volatile lifetime. Use 'RouteToThisEndpoint' instead.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor RouteToThisInstanceNotAllowed = new DiagnosticDescriptor(
             id: RouteToThisInstanceNotAllowedId,
             title: "RouteToThisInstance is not supported in AWS Lambda",
             messageFormat: "AWS Lambda instances cannot be directly addressed as they have a highly volatile lifetime. Use 'RouteToThisEndpoint' instead.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor TransportTransactionModeNotAllowed = new DiagnosticDescriptor(
             id: TransportTransactionModeNotAllowedId,
             title: "TransportTransactionMode is not supported in AWS Lambda",
             messageFormat: "Transport TransactionMode is controlled by the AWS Lambda trigger and cannot be configured via the NServiceBus transport configuration API when using AWS Lambda.",
             category: DiagnosticCategory,
             defaultSeverity: DiagnosticSeverity.Error,
             isEnabledByDefault: true
            );
    }
}
