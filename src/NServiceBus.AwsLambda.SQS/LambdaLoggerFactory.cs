namespace NServiceBus.AwsLambda.SQS
{
    using NServiceBus.Logging;
    using System;

    class LambdaLoggerFactory : ILoggerFactory
    {
        LogLevel level;

        public LambdaLoggerFactory(LogLevel level) => this.level = level;

        public ILog GetLogger(Type type) => GetLogger(type.FullName);

        public ILog GetLogger(string name) => new LambdaLog(name, level);
    }
}