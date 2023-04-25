namespace NServiceBus
{
    using System;
    using System.Threading;

    static class ExceptionExtensions
    {
        public static bool IsCausedBy(this Exception ex, CancellationToken cancellationToken) => ex is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }
}