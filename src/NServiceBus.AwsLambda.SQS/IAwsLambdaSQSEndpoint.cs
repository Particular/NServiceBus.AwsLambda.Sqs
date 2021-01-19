namespace NServiceBus
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Amazon.Lambda.Core;
    using Amazon.Lambda.SQSEvents;

    /// <summary>
    /// An NServiceBus endpoint hosted in AWS Lambda which does not receive messages automatically but only handles
    /// messages explicitly passed to it by the caller.
    /// </summary>
    public interface IAwsLambdaSQSEndpoint
    {
        /// <summary>
        /// Processes a messages received from an SQS trigger using the NServiceBus message pipeline.
        /// </summary>
        Task Process(SQSEvent @event, ILambdaContext lambdaContext, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends the provided message.
        /// </summary>
        Task Send(object message, SendOptions options, ILambdaContext lambdaContext);

        /// <summary>
        /// Sends the provided message.
        /// </summary>
        Task Send(object message, ILambdaContext lambdaContext);

        /// <summary>
        /// Instantiates a message of type T and sends it.
        /// </summary>
        Task Send<T>(Action<T> messageConstructor, SendOptions options, ILambdaContext lambdaContext);

        /// <summary>
        /// Instantiates a message of type T and sends it.
        /// </summary>
        Task Send<T>(Action<T> messageConstructor, ILambdaContext lambdaContext);

        /// <summary>
        /// Publish the message to subscribers.
        /// </summary>
        Task Publish(object message, PublishOptions options, ILambdaContext lambdaContext);

        /// <summary>
        /// Instantiates a message of type T and publishes it.
        /// </summary>
        Task Publish<T>(Action<T> messageConstructor, PublishOptions options, ILambdaContext lambdaContext);

        /// <summary>
        /// Instantiates a message of type T and publishes it.
        /// </summary>
        Task Publish(object message, ILambdaContext lambdaContext);

        /// <summary>
        /// Instantiates a message of type T and publishes it.
        /// </summary>
        Task Publish<T>(Action<T> messageConstructor, ILambdaContext lambdaContext);

        /// <summary>
        /// Subscribes to receive published messages of the specified type.
        /// This method is only necessary if you turned off auto-subscribe.
        /// </summary>
        Task Subscribe(Type eventType, SubscribeOptions options, ILambdaContext lambdaContext);

        /// <summary>
        /// Subscribes to receive published messages of the specified type.
        /// This method is only necessary if you turned off auto-subscribe.
        /// </summary>
        Task Subscribe(Type eventType, ILambdaContext lambdaContext);

        /// <summary>
        /// Unsubscribes to receive published messages of the specified type.
        /// </summary>
        Task Unsubscribe(Type eventType, UnsubscribeOptions options, ILambdaContext lambdaContext);

        /// <summary>
        /// Unsubscribes to receive published messages of the specified type.
        /// </summary>
        Task Unsubscribe(Type eventType, ILambdaContext lambdaContext);

    }
}