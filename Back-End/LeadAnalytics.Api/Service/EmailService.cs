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

    public Task SendInvitationAsync(
        string toEmail,
        string inviterName,
        string unitName,
        string role,
        string acceptUrl,
        int validHours)
    {
        var subject = $"Convite para acessar {unitName} · Doutor Digital";

        var roleLabel = role switch
        {
            "sdr" => "SDR",
            "manager" => "Gerente",
            "unit_user" => "Usuário",
            _ => role
        };

        var html = $@"
<!doctype html>
<html lang=""pt-br"">
  <body style=""font-family: Arial, Helvetica, sans-serif; background:#f5f6fa; margin:0; padding:24px;"">
    <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""max-width:560px; margin:0 auto; background:#ffffff; border-radius:12px; overflow:hidden; border:1px solid #e5e7eb;"">
      <tr>
        <td style=""padding:24px 32px; background:#0f172a; color:#fff;"">
          <h2 style=""margin:0; font-size:18px;"">Doutor Digital · Convite</h2>
        </td>
      </tr>
      <tr>
        <td style=""padding:32px;"">
          <p style=""margin:0 0 12px; color:#111827;""><strong>{WebUtility.HtmlEncode(inviterName)}</strong> convidou você para acessar o painel.</p>
          <p style=""margin:0 0 16px; color:#374151;"">Você terá acesso à unidade <strong>{WebUtility.HtmlEncode(unitName)}</strong> com o papel <strong>{WebUtility.HtmlEncode(roleLabel)}</strong>.</p>
          <div style=""text-align:center; margin:28px 0;"">
            <a href=""{WebUtility.HtmlEncode(acceptUrl)}"" style=""display:inline-block; background:#0077CC; color:#fff; text-decoration:none; padding:12px 22px; border-radius:8px; font-weight:600;"">Aceitar convite</a>
          </div>
          <p style=""margin:0 0 12px; color:#374151;"">O convite expira em <strong>{validHours} horas</strong>.</p>
          <p style=""margin:0 0 12px; color:#6b7280; font-size:13px;"">Você precisará entrar com a sua conta Google ({WebUtility.HtmlEncode(toEmail)}). Se este email não bater, fale com quem te convidou.</p>
          <p style=""margin:0; color:#6b7280; font-size:12px;"">Se o botão não funcionar, copie e cole este link no navegador:<br>{WebUtility.HtmlEncode(acceptUrl)}</p>
        </td>
      </tr>
      <tr>
        <td style=""padding:16px 32px; background:#f9fafb; color:#9ca3af; font-size:12px; text-align:center;"">
          © Doutor Digital — mensagem automática.
        </td>
      </tr>
    </table>
  </body>
</html>";

        var text =
            $"{inviterName} convidou você para o painel Doutor Digital.\n\n" +
            $"Unidade: {unitName}\n" +
            $"Papel: {roleLabel}\n\n" +
            $"Aceite em: {acceptUrl}\n\n" +
            $"Expira em {validHours} horas.\n";

        return SendAsync(toEmail, subject, html, text);
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
