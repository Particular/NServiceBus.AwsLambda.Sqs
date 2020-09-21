namespace NServiceBus.AwsLambda.SQS
{
    using NServiceBus.Logging;

    class LambdaLoggerDefinition : LoggingFactoryDefinition
    {
        LogLevel level = LogLevel.Info;

        public void Level(LogLevel level)
        {
            this.level = level;
        }

        protected override ILoggerFactory GetLoggingFactory()
        {
            return new LambdaLoggerFactory(level);
        }
    }
}