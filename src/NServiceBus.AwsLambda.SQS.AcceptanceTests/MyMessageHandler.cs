namespace NServiceBus.AwsLambda.SQS.AcceptanceTests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public class MyMessageHandler : IHandleMessages<MyMessage>
    {
        public Task Handle(MyMessage message, IMessageHandlerContext context)
        {
            if (Interlocked.Increment(ref invocationCounter) < 3)
            {
                throw new InvalidOperationException();
            }
            return Task.CompletedTask;
        }

        static int invocationCounter;
    }
}