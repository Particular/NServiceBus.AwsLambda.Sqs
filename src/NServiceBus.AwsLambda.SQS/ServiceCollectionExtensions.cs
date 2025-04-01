namespace NServiceBus.AwsLambda.SQS;

using System;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static void AddAwsLambdaNServiceBusEndpoint(this IServiceCollection services, string endpointName, Action<AwsLambdaSQSEndpointConfiguration, ILambdaContext> configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName, nameof(endpointName));

        var endpoint = new AwsLambdaSQSEndpoint(context =>
        {
            var endpointConfiguration = new AwsLambdaSQSEndpointConfiguration(endpointName);
            configure?.Invoke(endpointConfiguration, context);
            return endpointConfiguration;
        });

        _ = services.AddSingleton(endpoint);
    }
}