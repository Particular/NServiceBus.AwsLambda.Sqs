namespace NServiceBus.AwsLambda.SQS
{
    using System;
    using System.Text;

    static class QueueNameHelper
    {
        public static string GetSanitizedQueueName(string queueName)
        {
            if (queueName.Length > 80)
            {
                throw new Exception($"Address {queueName} is longer than 80 characters and therefore cannot be used to create an SQS queue. Use a shorter queue name.");
            }

            var queueNameBuilder = new StringBuilder(queueName);

            var skipCharacters = queueName.EndsWith(".fifo") ? 5 : 0;
            // SQS queue names can only have alphanumeric characters, hyphens and underscores.
            // Any other characters will be replaced with a hyphen.
            for (var i = 0; i < queueNameBuilder.Length - skipCharacters; ++i)
            {
                var c = queueNameBuilder[i];
                if (!char.IsLetterOrDigit(c)
                    && c != '-'
                    && c != '_')
                {
                    queueNameBuilder[i] = '-';
                }
            }

            return queueNameBuilder.ToString();
        }
    }
}