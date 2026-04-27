using System.Net;
using System.Net.Mail;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service;

public class EmailService(IOptions<SmtpOptions> options, ILogger<EmailService> logger)
{
    private readonly SmtpOptions _options = options.Value;
    private readonly ILogger<EmailService> _logger = logger;

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string? textBody = null)
    {
        if (string.IsNullOrWhiteSpace(_options.Host) ||
            string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            _logger.LogWarning("📭 SMTP não configurado. Pulei envio para {To}. Assunto: {Subject}", toEmail, subject);
            return;
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.Username, _options.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(toEmail));

        if (!string.IsNullOrWhiteSpace(textBody))
        {
            var plainView = AlternateView.CreateAlternateViewFromString(textBody, null, "text/plain");
            message.AlternateViews.Add(plainView);
        }

        try
        {
            await client.SendMailAsync(message);
            _logger.LogInformation("📨 Email enviado para {To} | Assunto: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Falha ao enviar email para {To}", toEmail);
            throw;
        }
    }

    public Task SendPasswordResetCodeAsync(string toEmail, string userName, string code, int validMinutes)
    {
        var subject = "Código de recuperação de senha";

        var html = $@"
<!doctype html>
<html lang=""pt-br"">
  <body style=""font-family: Arial, Helvetica, sans-serif; background:#f5f6fa; margin:0; padding:24px;"">
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:520px; margin:0 auto; background:#ffffff; border-radius:12px; overflow:hidden; border:1px solid #e5e7eb;"">
      <tr>
        <td style=""padding:24px 32px; background:#0f172a; color:#fff;"">
          <h2 style=""margin:0; font-size:18px;"">Doutor Digital</h2>
        </td>
      </tr>
      <tr>
        <td style=""padding:32px;"">
          <p style=""margin:0 0 12px; color:#111827;"">Olá, <strong>{WebUtility.HtmlEncode(userName)}</strong>.</p>
          <p style=""margin:0 0 16px; color:#374151;"">Recebemos uma solicitação para redefinir a senha da sua conta. Use o código abaixo para concluir o processo:</p>
          <div style=""text-align:center; margin:24px 0;"">
            <span style=""display:inline-block; font-size:32px; letter-spacing:8px; font-weight:700; color:#0f172a; background:#f1f5f9; padding:14px 24px; border-radius:10px; border:1px solid #e2e8f0;"">{code}</span>
          </div>
          <p style=""margin:0 0 12px; color:#374151;"">O código expira em <strong>{validMinutes} minutos</strong>.</p>
          <p style=""margin:0; color:#6b7280; font-size:13px;"">Se você não solicitou a recuperação, basta ignorar este email.</p>
        </td>
      </tr>
      <tr>
        <td style=""padding:16px 32px; background:#f9fafb; color:#9ca3af; font-size:12px; text-align:center;"">
          © Doutor Digital — mensagem automática, não responda.
        </td>
      </tr>
    </table>
  </body>
</html>";

        var text =
            $"Olá {userName},\n\n" +
            $"Use o código abaixo para redefinir sua senha:\n\n{code}\n\n" +
            $"O código expira em {validMinutes} minutos.\n\n" +
            "Se não foi você que solicitou, ignore este email.\n";

        return SendAsync(toEmail, subject, html, text);
    }
}
