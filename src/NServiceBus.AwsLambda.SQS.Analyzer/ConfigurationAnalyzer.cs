namespace NServiceBus.AwsLambda.SQS.Analyzer
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NServiceBus.AwsLambda.SQS.Analyzer.Extensions;

    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ConfigurationAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(
            AwsLambdaDiagnostics.PurgeOnStartupNotAllowed,
            AwsLambdaDiagnostics.LimitMessageProcessingToNotAllowed,
            AwsLambdaDiagnostics.DefineCriticalErrorActionNotAllowed,
            AwsLambdaDiagnostics.SetDiagnosticsPathNotAllowed,
            AwsLambdaDiagnostics.MakeInstanceUniquelyAddressableNotAllowed,
            AwsLambdaDiagnostics.UseTransportNotAllowed,
            AwsLambdaDiagnostics.OverrideLocalAddressNotAllowed,
            AwsLambdaDiagnostics.RouteReplyToThisInstanceNotAllowed,
            AwsLambdaDiagnostics.RouteToThisInstanceNotAllowed,
            AwsLambdaDiagnostics.TransportTransactionModeNotAllowed
        );

        static readonly Dictionary<string, DiagnosticDescriptor> NotAllowedEndpointConfigurationMethods
            = new Dictionary<string, DiagnosticDescriptor>
            {
                ["PurgeOnStartup"] = AwsLambdaDiagnostics.PurgeOnStartupNotAllowed,
                ["LimitMessageProcessingConcurrencyTo"] = AwsLambdaDiagnostics.LimitMessageProcessingToNotAllowed,
                ["DefineCriticalErrorAction"] = AwsLambdaDiagnostics.DefineCriticalErrorActionNotAllowed,
                ["SetDiagnosticsPath"] = AwsLambdaDiagnostics.SetDiagnosticsPathNotAllowed,
                ["MakeInstanceUniquelyAddressable"] = AwsLambdaDiagnostics.MakeInstanceUniquelyAddressableNotAllowed,
                ["UseTransport"] = AwsLambdaDiagnostics.UseTransportNotAllowed,
                ["OverrideLocalAddress"] = AwsLambdaDiagnostics.OverrideLocalAddressNotAllowed,
            };

        static readonly Dictionary<string, DiagnosticDescriptor> NotAllowedSendAndReplyOptions
            = new Dictionary<string, DiagnosticDescriptor>
            {
                ["RouteReplyToThisInstance"] = AwsLambdaDiagnostics.RouteReplyToThisInstanceNotAllowed,
                ["RouteToThisInstance"] = AwsLambdaDiagnostics.RouteToThisInstanceNotAllowed,
            };

        static readonly Dictionary<string, DiagnosticDescriptor> NotAllowedTransportSettings
            = new Dictionary<string, DiagnosticDescriptor>
            {
                ["TransportTransactionMode"] = AwsLambdaDiagnostics.TransportTransactionModeNotAllowed,
                ["Transactions"] = AwsLambdaDiagnostics.TransportTransactionModeNotAllowed
            };

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(AnalyzeTransport, SyntaxKind.SimpleMemberAccessExpression);
        }

        static void Analyze(SyntaxNodeAnalysisContext context)
        {
            if (context.Node is not InvocationExpressionSyntax invocationExpression)
            {
                return;
            }

            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccessExpression)
            {
                return;
            }

            AnalyzeEndpointConfiguration(context, invocationExpression, memberAccessExpression);

            AnalyzeSendAndReplyOptions(context, invocationExpression, memberAccessExpression);

            AnalyzeTransportExtensions(context, invocationExpression, memberAccessExpression);
        }

        static void AnalyzeTransport(SyntaxNodeAnalysisContext context)
        {
            if (context.Node is not MemberAccessExpressionSyntax memberAccess)
            {
                return;
            }

            if (!NotAllowedTransportSettings.TryGetValue(memberAccess.Name.ToString(), out var diagnosticDescriptor))
            {
                return;

            }

            var memberAccessSymbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);

            if (memberAccessSymbol.Symbol is not IPropertySymbol propertySymbol)
            {
                return;
            }

            var containingType = propertySymbol.ContainingType.ToString();
            if (containingType is "NServiceBus.SqsTransport" or "NServiceBus.Transport.TransportDefinition")
            {
                context.ReportDiagnostic(diagnosticDescriptor, memberAccess);

            }
        }

        static void AnalyzeEndpointConfiguration(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpression, MemberAccessExpressionSyntax memberAccessExpression)
        {
            if (!NotAllowedEndpointConfigurationMethods.TryGetValue(memberAccessExpression.Name.Identifier.Text, out var diagnosticDescriptor))
            {
                return;
            }

            var memberAccessSymbol = context.SemanticModel.GetSymbolInfo(memberAccessExpression, context.CancellationToken);

            if (memberAccessSymbol.Symbol is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.ReceiverType?.ToString() == "NServiceBus.EndpointConfiguration")
            {
                context.ReportDiagnostic(diagnosticDescriptor, invocationExpression);
            }
        }

        static void AnalyzeSendAndReplyOptions(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpression, MemberAccessExpressionSyntax memberAccessExpression)
        {
            if (!NotAllowedSendAndReplyOptions.TryGetValue(memberAccessExpression.Name.Identifier.Text, out var diagnosticDescriptor))
            {
                return;
            }

            var memberAccessSymbol = context.SemanticModel.GetSymbolInfo(memberAccessExpression, context.CancellationToken);

            if (memberAccessSymbol.Symbol is not IMethodSymbol methodSymbol)
            {
                return;
            }

            var receiverType = methodSymbol.ReceiverType?.ToString();
            if (receiverType is "NServiceBus.SendOptions" or "NServiceBus.ReplyOptions")
            {
                context.ReportDiagnostic(diagnosticDescriptor, invocationExpression);
            }
        }

        static void AnalyzeTransportExtensions(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocationExpression, MemberAccessExpressionSyntax memberAccessExpression)
        {
            if (!NotAllowedTransportSettings.TryGetValue(memberAccessExpression.Name.Identifier.Text, out var diagnosticDescriptor))
            {
                return;
            }

            var memberAccessSymbol = context.SemanticModel.GetSymbolInfo(memberAccessExpression, context.CancellationToken);

            if (memberAccessSymbol.Symbol is not IMethodSymbol methodSymbol)
            {
                return;
            }

            if (methodSymbol.ReceiverType?.ToString() == "NServiceBus.TransportExtensions<NServiceBus.SqsTransport>")
            {
                context.ReportDiagnostic(diagnosticDescriptor, invocationExpression);
            }
        }
    }
}