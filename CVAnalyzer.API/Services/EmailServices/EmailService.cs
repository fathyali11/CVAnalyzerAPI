using CVAnalyzerAPI.Consts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CVAnalyzerAPI.Services.EmailServices;

public class EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger) : IEmailService
{
    private readonly EmailSettings _emailSettings = options.Value;

    public async Task SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var email = new MimeMessage();
        email.From.Add(new MailboxAddress(_emailSettings.SenderName, _emailSettings.SenderEmail));
        email.To.Add(MailboxAddress.Parse(toEmail));
        email.Subject = subject;
        var builder = new BodyBuilder { HtmlBody = body };
        email.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        try
        {
            
            await smtp.ConnectAsync(_emailSettings.Host, _emailSettings.Port, SecureSocketOptions.StartTls, cancellationToken);
            await smtp.AuthenticateAsync(_emailSettings.Username, _emailSettings.Password, cancellationToken);
            await smtp.SendAsync(email, cancellationToken);
            
            logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {ToEmail}", toEmail);
        }
        finally
        {
            await smtp.DisconnectAsync(true, cancellationToken);
        }
    }
}