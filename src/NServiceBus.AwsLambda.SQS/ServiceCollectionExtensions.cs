namespace NServiceBus;

using System;
using Amazon.Lambda.Core;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add an NServiceBus serverless endpoint to the application services
    /// </summary>
    public static void AddAwsLambdaSQSEndpoint(this IServiceCollection services, string endpointName, Action<AwsLambdaSQSEndpointConfiguration, ILambdaContext> configure = null)
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