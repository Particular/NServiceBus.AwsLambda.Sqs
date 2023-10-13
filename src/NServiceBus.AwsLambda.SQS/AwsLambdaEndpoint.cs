namespace NServiceBus
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Core;
    using Amazon.Lambda.SQSEvents;
    using Amazon.SQS;
    using AwsLambda.SQS.TransportWrapper;
    using Logging;

    /// <summary>
    /// An NServiceBus endpoint hosted in AWS Lambda which does not receive messages automatically but only handles
    /// messages explicitly passed to it by the caller.
    /// </summary>
    public class AwsLambdaSQSEndpoint : IAwsLambdaSQSEndpoint
    {
        /// <summary>
        /// Create a new endpoint hosting in AWS Lambda.
        /// </summary>
        public AwsLambdaSQSEndpoint(Func<ILambdaContext, AwsLambdaSQSEndpointConfiguration> configurationFactory)
        {
            this.configurationFactory = configurationFactory;
        }

        /// <summary>
        /// Processes a messages received from an SQS trigger using the NServiceBus message pipeline.
        /// </summary>
        public async Task Process(SQSEvent @event, ILambdaContext lambdaContext, CancellationToken cancellationToken = default)
        {
            // enforce early initialization instead of lazy during process so that the necessary clients can be created.
            await InitializeEndpointIfNecessary(lambdaContext, cancellationToken)
                .ConfigureAwait(false);

            if (isSendOnly)
            {
                throw new InvalidOperationException(
                    $"This endpoint cannot process messages because it is configured in send-only mode. Remove the '{nameof(EndpointConfiguration)}.{nameof(EndpointConfiguration.SendOnly)}' configuration.'"
                    );
            }

            var processTasks = new List<Task>();
            foreach (var receivedMessage in @event.Records)
            {
                processTasks.Add(processor.ProcessMessage(receivedMessage, cancellationToken));
            }

            await Task.WhenAll(processTasks)
                .ConfigureAwait(false);
        }

        async Task InitializeEndpointIfNecessary(ILambdaContext executionContext, CancellationToken token = default)
        {
            if (processor == null)
            {
                await semaphoreLock.WaitAsync(token)
                    .ConfigureAwait(false);
                try
                {
                    processor ??= await Initialize(executionContext, token).ConfigureAwait(false);
                }
                finally
                {
                    _ = semaphoreLock.Release();
                }
            }
        }

        async Task<AwsLambdaSQSEndpointMessageProcessor> Initialize(ILambdaContext executionContext, CancellationToken token)
        {
            var configuration = configurationFactory(executionContext);

            sqsClient = configuration.Transport.SqsClient;
            s3Settings = configuration.Transport.S3;

            var serverlessTransport = new ServerlessTransport(configuration.Transport);
            configuration.EndpointConfiguration.UseTransport(serverlessTransport);

            endpoint = await Endpoint.Start(configuration.EndpointConfiguration, token).ConfigureAwait(false);

            var transportInfrastructure = serverlessTransport.GetTransportInfrastructure(endpoint);
            isSendOnly = transportInfrastructure.IsSendOnly;

            string receiveQueueAddress = null;
            string receiveQueueUrl = null;
            string errorQueueUrl = null;

            if (!isSendOnly)
            {
                receiveQueueAddress = transportInfrastructure.PipelineInvoker.ReceiveAddress;
                receiveQueueUrl = await GetQueueUrl(receiveQueueAddress).ConfigureAwait(false);
                errorQueueUrl = await GetQueueUrl(transportInfrastructure.ErrorQueueAddress).ConfigureAwait(false);
            }

            return new AwsLambdaSQSEndpointMessageProcessor(transportInfrastructure.PipelineInvoker, sqsClient,
                s3Settings, receiveQueueAddress, receiveQueueUrl, errorQueueUrl);
        }

        /// <inheritdoc />
        public async Task Send(object message, SendOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext).ConfigureAwait(false);

            await endpoint.Send(message, options).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task Send(object message, ILambdaContext lambdaContext)
            => Send(message, new SendOptions(), lambdaContext);

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, SendOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Send(messageConstructor, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Send<T>(Action<T> messageConstructor, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await Send(messageConstructor, new SendOptions(), lambdaContext)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, PublishOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(message, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, PublishOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(messageConstructor, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish(object message, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(message)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Publish<T>(Action<T> messageConstructor, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Publish(messageConstructor)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, SubscribeOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Subscribe(eventType, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Subscribe(Type eventType, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Subscribe(eventType)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, UnsubscribeOptions options, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType, options)
                .ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task Unsubscribe(Type eventType, ILambdaContext lambdaContext)
        {
            await InitializeEndpointIfNecessary(lambdaContext)
                .ConfigureAwait(false);

            await endpoint.Unsubscribe(eventType)
                .ConfigureAwait(false);
        }

        async Task<string> GetQueueUrl(string queueName)
        {
            try
            {
                return (await sqsClient.GetQueueUrlAsync(queueName).ConfigureAwait(false)).QueueUrl;
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to obtain the queue URL for queue {queueName} (derived from configured name {queueName}).", e);
                throw;
            }
        }

        bool isSendOnly;
        readonly Func<ILambdaContext, AwsLambdaSQSEndpointConfiguration> configurationFactory;
        readonly SemaphoreSlim semaphoreLock = new(initialCount: 1, maxCount: 1);
        AwsLambdaSQSEndpointMessageProcessor processor;

        IEndpointInstance endpoint;
        IAmazonSQS sqsClient;
        S3Settings s3Settings;

        static readonly ILog Logger = LogManager.GetLogger(typeof(AwsLambdaSQSEndpoint));
    }
}