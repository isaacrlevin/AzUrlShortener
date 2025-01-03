using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;

namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class EmailService
    {
        private EmailClient _emailClient;
        private readonly ILogger _logger;
        private readonly ShortenerSettings _settings;
        public EmailService(ILoggerFactory loggerFactory, ShortenerSettings settings)
        {
            _logger = loggerFactory.CreateLogger<EmailService>();
            _settings = settings;

            _emailClient = new EmailClient(_settings.COMMUNICATION_SERVICES_CONNECTION_STRING);
        }

        public async Task SendExceptionEmail(string subject, Exception ex)
        {
            if (ex == null)
            {
                return;
            }

            string htmlContent = $@"
                <html>
                <body>
                    <h1>Exception Type: {ex.GetType().Name}</h1>
                    <p>Exception Message: {ex.Message}</p>
                    <p>StackTrace: {ex.StackTrace}</p>
                </body>
                </html>";

            /// Send the email message with WaitUntil.Started
            EmailSendOperation emailSendOperation = await _emailClient.SendAsync(
                Azure.WaitUntil.Started,
                _settings.EmailFrom,
                _settings.EmailTo,
                subject,
                htmlContent);

            /// Call UpdateStatus on the email send operation to poll for the status
            /// manually.
            try
            {
                while (true)
                {
                    await emailSendOperation.UpdateStatusAsync();
                    if (emailSendOperation.HasCompleted)
                    {
                        break;
                    }
                    await Task.Delay(100);
                }

                if (emailSendOperation.HasValue)
                {
                    Console.WriteLine($"Email queued for delivery. Status = {emailSendOperation.Value.Status}");
                }
            }
            catch (RequestFailedException ex)
            {
                Console.WriteLine($"Email send failed with Code = {ex.ErrorCode} and Message = {ex.Message}");
            }

            /// Get the OperationId so that it can be used for tracking the message for troubleshooting
            string operationId = emailSendOperation.Id;
            Console.WriteLine($"Email operation id = {operationId}");
        }
    }
}
