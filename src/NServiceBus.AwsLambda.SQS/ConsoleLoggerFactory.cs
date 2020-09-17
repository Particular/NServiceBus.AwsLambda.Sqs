namespace NServiceBus.AwsLambda.SQS
{
    using NServiceBus.Logging;
    using System;

    class ConsoleLoggerFactory : ILoggerFactory
    {
        LogLevel level;

        public ConsoleLoggerFactory(LogLevel level)
        {
            this.level = level;
        }

        public ILog GetLogger(Type type)
        {
            return GetLogger(type.FullName);
        }

        public ILog GetLogger(string name)
        {
            return new ConsoleLog(name, level);
        }
    }
}