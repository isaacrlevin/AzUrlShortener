using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using System.Text;

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

        private string StackTraceToString(Exception ex)
        {
            StringBuilder stringBuilder = new StringBuilder();
            foreach (var line in ex.StackTrace.Split(Environment.NewLine))
            {
               stringBuilder.AppendLine("    {line}");
            }
            return stringBuilder.ToString();
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
                    <p>StackTrace: {StackTraceToString(ex)}</p>
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
                    _logger.LogInformation($"Email queued for delivery. Status = {emailSendOperation.Value.Status}");
                }
            }
            catch (RequestFailedException exception)
            {
               _logger.LogError($"Email send failed with Code = {exception.ErrorCode} and Message = {exception.Message}");
            }

            /// Get the OperationId so that it can be used for tracking the message for troubleshooting
            string operationId = emailSendOperation.Id;
            _logger.LogInformation($"Email operation id = {operationId}");
        }
    }
}
