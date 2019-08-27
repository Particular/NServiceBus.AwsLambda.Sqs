namespace NServiceBus.AwsLambda.SQS.AcceptanceTests
{
    using System;
    using System.Threading.Tasks;

    public class MyAlwaysThrowingMessageHandler : IHandleMessages<MyAlwaysThrowingMessage>
    {
        public Task Handle(MyAlwaysThrowingMessage message, IMessageHandlerContext context)
        {
            throw new InvalidOperationException();
        }
    }
}