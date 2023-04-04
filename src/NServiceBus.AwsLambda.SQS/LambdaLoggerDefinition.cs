namespace NServiceBus.AwsLambda.SQS
{
    using NServiceBus.Logging;

    class LambdaLoggerDefinition : LoggingFactoryDefinition
    {
        public void Level(LogLevel level) => this.level = level;

        protected override ILoggerFactory GetLoggingFactory() => new LambdaLoggerFactory(level);

        LogLevel level = LogLevel.Info;
    }
}