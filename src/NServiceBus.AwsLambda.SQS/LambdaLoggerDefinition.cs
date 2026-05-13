namespace NServiceBus.AwsLambda.SQS
{
    using NServiceBus.Logging;

#pragma warning disable CS0618 // Type or member is obsolete
    class LambdaLoggerDefinition : LoggingFactoryDefinition
#pragma warning restore CS0618 // Type or member is obsolete
    {
        public void Level(LogLevel level) => this.level = level;

        protected override ILoggerFactory GetLoggingFactory() => new LambdaLoggerFactory(level);

        LogLevel level = LogLevel.Info;
    }
}