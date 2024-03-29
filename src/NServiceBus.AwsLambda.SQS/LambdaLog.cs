namespace NServiceBus.AwsLambda.SQS
{
    using System;
    using Amazon.Lambda.Core;
    using NServiceBus.Logging;

    using LogLevel = Logging.LogLevel;

    class LambdaLog : ILog
    {
        string name;
        public bool IsDebugEnabled { get; }
        public bool IsInfoEnabled { get; }
        public bool IsWarnEnabled { get; }
        public bool IsErrorEnabled { get; }
        public bool IsFatalEnabled { get; }

        public LambdaLog(string name, LogLevel level)
        {
            this.name = name;

            IsDebugEnabled = level <= LogLevel.Debug;
            IsInfoEnabled = level <= LogLevel.Info;
            IsWarnEnabled = level <= LogLevel.Warn;
            IsErrorEnabled = level <= LogLevel.Error;
            IsFatalEnabled = level <= LogLevel.Fatal;
        }

        void Write(string level, string message, Exception exception)
            => LambdaLogger.Log($"{name}. {level}. {message}. Exception: {exception}");

        void Write(string level, string message)
            => LambdaLogger.Log($"{name}. {level}. {message}.");

        void Write(string level, string format, params object[] args)
        {
            format = $"{name}. {level}. {format}";
            LambdaLogger.Log(string.Format(format, args));
        }

        public void Debug(string message)
        {
            if (IsDebugEnabled)
            {
                Write("Debug", message);
            }
        }

        public void Debug(string message, Exception exception)
        {
            if (IsDebugEnabled)
            {
                Write("Debug", message, exception);
            }
        }

        public void DebugFormat(string format, params object[] args)
        {
            if (IsDebugEnabled)
            {
                Write("Debug", format, args);
            }
        }

        public void Info(string message)
        {
            if (IsInfoEnabled)
            {
                Write("Info", message);
            }
        }

        public void Info(string message, Exception exception)
        {
            if (IsInfoEnabled)
            {
                Write("Info", message, exception);
            }
        }

        public void InfoFormat(string format, params object[] args)
        {
            if (IsInfoEnabled)
            {
                Write("Info", format, args);
            }
        }

        public void Warn(string message)
        {
            if (IsWarnEnabled)
            {
                Write("Warn", message);
            }
        }

        public void Warn(string message, Exception exception)
        {
            if (IsWarnEnabled)
            {
                Write("Warn", message, exception);
            }
        }

        public void WarnFormat(string format, params object[] args)
        {
            if (IsWarnEnabled)
            {
                Write("Warn", format, args);
            }
        }

        public void Error(string message)
        {
            if (IsErrorEnabled)
            {
                Write("Error", message);
            }
        }

        public void Error(string message, Exception exception)
        {
            if (IsErrorEnabled)
            {
                Write("Error", message, exception);
            }
        }

        public void ErrorFormat(string format, params object[] args)
        {
            if (IsErrorEnabled)
            {
                Write("Error", format, args);
            }
        }

        public void Fatal(string message)
        {
            if (IsFatalEnabled)
            {
                Write("Fatal", message);
            }
        }

        public void Fatal(string message, Exception exception)
        {
            if (IsFatalEnabled)
            {
                Write("Fatal", message, exception);
            }
        }

        public void FatalFormat(string format, params object[] args)
        {
            if (IsFatalEnabled)
            {
                Write("Fatal", format, args);
            }
        }
    }
}