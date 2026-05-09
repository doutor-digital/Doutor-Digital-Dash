using System.Net;
using System.Net.Mail;
using LeadAnalytics.Api.Options;
using Microsoft.Extensions.Options;

namespace LeadAnalytics.Api.Service;

/// <summary>
/// Camada de envio de emails. Tenta Resend primeiro (HTTPS API, melhor
/// entregabilidade); cai pra SMTP se Resend não estiver configurado.
///
/// Templates são desenhados pra:
///   - Lato (corpo) + Inter (display) via Google Fonts CDN
///   - Logo Doutor Digital destacada no topo
///   - Layout em tabela de 600px (compatibilidade Outlook/Gmail/Apple Mail)
///   - Plain text junto pra reduzir sinal de spam
/// </summary>
public class EmailService
{
    private const string LogoUrl =
        "https://doutordigitalconsultoria.com/wp-content/uploads/2026/04/Copia-de-logo-cor-original.png";
    private const string BrandPrimary = "#0077CC";
    private const string BrandPrimaryDark = "#005EA6";
    private const string BrandText = "#0F172A";
    private const string BrandMuted = "#64748B";
    private const string BrandBg = "#F4F7FB";

    private readonly SmtpOptions _smtp;
    private readonly ResendClient _resend;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<SmtpOptions> smtpOptions,
        ResendClient resend,
        ILogger<EmailService> logger)
    {
        _smtp = smtpOptions.Value;
        _resend = resend;
        _logger = logger;
    }

    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        string? textBody = null,
        CancellationToken ct = default)
    {
        // 1) Resend: caminho preferido (HTTPS, DKIM se domínio verificado).
        if (_resend.IsConfigured)
        {
            var ok = await _resend.SendAsync(toEmail, subject, htmlBody, textBody, ct: ct);
            if (ok) return;
            _logger.LogWarning("Resend falhou para {To} — tentando SMTP fallback", toEmail);
        }

        // 2) SMTP fallback (mantém compat com config antiga).
        if (string.IsNullOrWhiteSpace(_smtp.Host) ||
            string.IsNullOrWhiteSpace(_smtp.FromAddress))
        {
            _logger.LogWarning("📭 Sem provedor de email configurado. Pulei {To}: {Subject}", toEmail, subject);
            return;
        }

        using var client = new SmtpClient(_smtp.Host, _smtp.Port)
        {
            EnableSsl = _smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_smtp.Username, _smtp.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_smtp.FromAddress, _smtp.FromName),
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
            await client.SendMailAsync(message, ct);
            _logger.LogInformation("📨 SMTP enviou para {To}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Falha SMTP para {To}", toEmail);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Convite
    // ─────────────────────────────────────────────────────────────────────

    public Task SendInvitationAsync(
        string toEmail,
        string inviterName,
        string unitName,
        string role,
        string acceptUrl,
        int validHours)
    {
        var subject = $"{inviterName} convidou você para o painel da Doutor Digital";

        var roleLabel = role switch
        {
            "sdr" => "SDR",
            "manager" => "Gerente",
            "unit_user" => "Usuário",
            _ => role
        };

        var html = BuildEmailShell(
            preheader: $"{inviterName} te convidou para acessar a unidade {unitName}.",
            heading: $"Você foi convidado, {GetFirstName(toEmail)}",
            bodyHtml: $@"
            <p style=""margin:0 0 16px;font-family:'Lato',Arial,sans-serif;font-size:15px;line-height:1.6;color:{BrandText};"">
              <strong>{WebUtility.HtmlEncode(inviterName)}</strong> está convidando você
              para acessar o painel da <strong>Doutor Digital</strong>.
            </p>

            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px 0;"">
              <tr>
                <td style=""padding:16px 18px;background:#EFF6FF;border:1px solid #DBEAFE;border-radius:10px;"">
                  <p style=""margin:0;font-family:'Inter',Arial,sans-serif;font-size:11px;letter-spacing:0.18em;text-transform:uppercase;color:{BrandMuted};"">Detalhes do convite</p>
                  <p style=""margin:6px 0 0;font-family:'Lato',Arial,sans-serif;font-size:14px;color:{BrandText};"">
                    <strong>Unidade:</strong> {WebUtility.HtmlEncode(unitName)}<br>
                    <strong>Papel:</strong> {WebUtility.HtmlEncode(roleLabel)}<br>
                    <strong>Email:</strong> {WebUtility.HtmlEncode(toEmail)}
                  </p>
                </td>
              </tr>
            </table>

            <p style=""margin:0 0 14px;font-family:'Lato',Arial,sans-serif;font-size:14px;line-height:1.6;color:{BrandText};"">
              Para acessar, clique no botão abaixo e entre com a sua conta Google
              cadastrada nesse mesmo email:
            </p>",
            ctaLabel: "Aceitar convite",
            ctaUrl: acceptUrl,
            footerHtml: $@"
            <p style=""margin:0 0 8px;font-family:'Inter',Arial,sans-serif;font-size:12px;color:{BrandMuted};"">
              O link expira em <strong>{validHours} horas</strong>.
              Se não foi você que recebeu este convite, pode ignorar este email.
            </p>
            <p style=""margin:8px 0 0;font-family:'Inter',Arial,sans-serif;font-size:11px;color:#94A3B8;word-break:break-all;"">
              Link direto: {WebUtility.HtmlEncode(acceptUrl)}
            </p>"
        );

        var text =
            $"Olá,\n\n" +
            $"{inviterName} te convidou para acessar o painel da Doutor Digital.\n\n" +
            $"Unidade: {unitName}\n" +
            $"Papel: {roleLabel}\n" +
            $"Email: {toEmail}\n\n" +
            $"Aceite seu convite (entre com a conta Google deste mesmo email):\n{acceptUrl}\n\n" +
            $"O link expira em {validHours} horas.\n\n" +
            $"Se não foi você, pode ignorar este email.\n";

        return SendAsync(toEmail, subject, html, text);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Boas-vindas (após aceite)
    // ─────────────────────────────────────────────────────────────────────

    public Task SendWelcomeAsync(
        string toEmail,
        string userName,
        string unitName,
        string dashboardUrl)
    {
        var subject = $"Bem-vindo à Doutor Digital, {GetFirstNameFromName(userName)}!";

        var html = BuildEmailShell(
            preheader: $"Sua conta foi ativada na unidade {unitName}.",
            heading: $"Bem-vindo, {GetFirstNameFromName(userName)}!",
            bodyHtml: $@"
            <p style=""margin:0 0 14px;font-family:'Lato',Arial,sans-serif;font-size:15px;line-height:1.6;color:{BrandText};"">
              Sua conta foi ativada no painel da <strong>Doutor Digital</strong>. Agora
              você está dentro da unidade <strong>{WebUtility.HtmlEncode(unitName)}</strong>
              e tem acesso a tudo que sua função permite.
            </p>

            <p style=""margin:18px 0 8px;font-family:'Inter',Arial,sans-serif;font-size:12px;letter-spacing:0.15em;text-transform:uppercase;color:{BrandMuted};"">
              O que você pode fazer agora
            </p>

            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">
              {RenderFeatureRow("Revisar leads", "Confira e aprove leads que chegaram pelos seus canais.")}
              {RenderFeatureRow("Acompanhar consultas", "Veja agendamentos, comparecimentos e tratamentos fechados.")}
              {RenderFeatureRow("Visualizar métricas", "Conversão, origem dos leads, performance por unidade.")}
              {RenderFeatureRow("Conectar integrações", "Cloudia, Meta Ads, n8n e mais — tudo num lugar só.")}
            </table>",
            ctaLabel: "Acessar o painel",
            ctaUrl: dashboardUrl,
            footerHtml: $@"
            <p style=""margin:0 0 8px;font-family:'Inter',Arial,sans-serif;font-size:12px;color:{BrandMuted};"">
              Precisa de ajuda? Responda este email — chega direto na nossa caixa.
            </p>"
        );

        var text =
            $"Bem-vindo à Doutor Digital, {userName}!\n\n" +
            $"Sua conta foi ativada na unidade {unitName}.\n\n" +
            $"O que você pode fazer agora:\n" +
            $"  • Revisar leads recebidos pela Cloudia\n" +
            $"  • Acompanhar consultas e tratamentos\n" +
            $"  • Visualizar métricas em tempo real\n" +
            $"  • Conectar integrações\n\n" +
            $"Acessar o painel: {dashboardUrl}\n\n" +
            $"Precisa de ajuda? Responda este email.\n";

        return SendAsync(toEmail, subject, html, text);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Reset de senha — usa o mesmo shell pra consistência
    // ─────────────────────────────────────────────────────────────────────

    public Task SendPasswordResetCodeAsync(string toEmail, string userName, string code, int validMinutes)
    {
        var subject = "Código de recuperação de senha — Doutor Digital";

        var html = BuildEmailShell(
            preheader: $"Use o código {code} para redefinir sua senha. Expira em {validMinutes} min.",
            heading: $"Olá, {GetFirstNameFromName(userName)}",
            bodyHtml: $@"
            <p style=""margin:0 0 14px;font-family:'Lato',Arial,sans-serif;font-size:15px;line-height:1.6;color:{BrandText};"">
              Recebemos uma solicitação para redefinir a senha da sua conta. Use o
              código abaixo para concluir o processo.
            </p>

            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:24px 0;"">
              <tr>
                <td align=""center"">
                  <div style=""display:inline-block;background:#F1F5F9;border:1px solid #E2E8F0;border-radius:12px;padding:18px 32px;font-family:'Inter',Arial,sans-serif;font-size:32px;letter-spacing:8px;font-weight:700;color:{BrandText};"">
                    {code}
                  </div>
                </td>
              </tr>
            </table>",
            ctaLabel: null,
            ctaUrl: null,
            footerHtml: $@"
            <p style=""margin:0 0 8px;font-family:'Inter',Arial,sans-serif;font-size:12px;color:{BrandMuted};"">
              O código expira em <strong>{validMinutes} minutos</strong>.
              Se não foi você, pode ignorar este email — sua senha continua a mesma.
            </p>"
        );

        var text =
            $"Olá, {userName},\n\n" +
            $"Use o código abaixo para redefinir sua senha:\n\n{code}\n\n" +
            $"Expira em {validMinutes} minutos.\n";

        return SendAsync(toEmail, subject, html, text);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Shell HTML (compartilhado por todos os templates)
    // ─────────────────────────────────────────────────────────────────────

    private static string BuildEmailShell(
        string preheader,
        string heading,
        string bodyHtml,
        string? ctaLabel,
        string? ctaUrl,
        string footerHtml)
    {
        var ctaBlock = string.IsNullOrWhiteSpace(ctaLabel) || string.IsNullOrWhiteSpace(ctaUrl)
            ? string.Empty
            : $@"
            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""margin:32px 0 8px;"">
              <tr>
                <td align=""center"">
                  <a href=""{WebUtility.HtmlEncode(ctaUrl)}""
                     style=""display:inline-block;background:linear-gradient(180deg,{BrandPrimary} 0%,{BrandPrimaryDark} 100%);color:#FFFFFF;text-decoration:none;font-family:'Inter',Arial,sans-serif;font-size:15px;font-weight:600;letter-spacing:0.01em;padding:14px 36px;border-radius:10px;box-shadow:0 6px 18px rgba(0,119,204,0.25);"">
                    {WebUtility.HtmlEncode(ctaLabel)}
                  </a>
                </td>
              </tr>
            </table>";

        return $@"<!doctype html>
<html lang=""pt-BR"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<meta name=""x-apple-disable-message-reformatting"">
<title>Doutor Digital</title>
<link href=""https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=Lato:wght@400;600;700&display=swap"" rel=""stylesheet"">
<style>
  @import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=Lato:wght@400;600;700&display=swap');
  body {{ margin:0; padding:0; background:{BrandBg}; }}
  a {{ color:{BrandPrimary}; }}
  img {{ border:0; outline:none; text-decoration:none; -ms-interpolation-mode:bicubic; }}
  table {{ border-collapse:collapse !important; }}
</style>
</head>
<body style=""margin:0;padding:0;background:{BrandBg};font-family:'Lato','Inter',Arial,Helvetica,sans-serif;"">

<!-- preheader (texto invisível que aparece na inbox preview) -->
<div style=""display:none;font-size:1px;color:{BrandBg};line-height:1px;max-height:0px;max-width:0px;opacity:0;overflow:hidden;"">
  {WebUtility.HtmlEncode(preheader)}
</div>

<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:{BrandBg};padding:32px 16px;"">
  <tr>
    <td align=""center"">
      <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#FFFFFF;border-radius:16px;overflow:hidden;box-shadow:0 8px 32px rgba(15,23,42,0.06);"">

        <!-- LOGO BAR -->
        <tr>
          <td align=""center"" style=""padding:36px 32px 24px;background:linear-gradient(135deg,#FFFFFF 0%,#F0F7FF 100%);border-bottom:1px solid #E2E8F0;"">
            <img src=""{LogoUrl}"" alt=""Doutor Digital"" width=""160"" style=""display:block;width:160px;max-width:60%;height:auto;"">
          </td>
        </tr>

        <!-- HEADING -->
        <tr>
          <td style=""padding:32px 36px 8px;"">
            <h1 style=""margin:0;font-family:'Inter','Lato',Arial,sans-serif;font-size:22px;font-weight:700;line-height:1.3;color:{BrandText};letter-spacing:-0.01em;"">
              {WebUtility.HtmlEncode(heading)}
            </h1>
          </td>
        </tr>

        <!-- BODY -->
        <tr>
          <td style=""padding:8px 36px 0;"">
            {bodyHtml}
          </td>
        </tr>

        <!-- CTA -->
        <tr>
          <td style=""padding:0 36px;"">
            {ctaBlock}
          </td>
        </tr>

        <!-- FOOTER NOTICE -->
        <tr>
          <td style=""padding:24px 36px 32px;border-top:1px solid #F1F5F9;"">
            {footerHtml}
          </td>
        </tr>

        <!-- BRAND FOOTER -->
        <tr>
          <td style=""padding:18px 36px;background:#F8FAFC;text-align:center;"">
            <p style=""margin:0;font-family:'Inter',Arial,sans-serif;font-size:11px;color:{BrandMuted};letter-spacing:0.04em;"">
              © {DateTime.UtcNow.Year} Doutor Digital · Mensagem automática · não responda se for um aviso de sistema.
            </p>
          </td>
        </tr>

      </table>
    </td>
  </tr>
</table>

</body>
</html>";
    }

    private static string RenderFeatureRow(string title, string desc) => $@"
              <tr>
                <td style=""padding:10px 0;"">
                  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"">
                    <tr>
                      <td width=""8"" style=""width:8px;background:{BrandPrimary};border-radius:4px;""></td>
                      <td style=""padding-left:14px;"">
                        <p style=""margin:0;font-family:'Inter','Lato',Arial,sans-serif;font-size:14px;font-weight:600;color:{BrandText};"">{WebUtility.HtmlEncode(title)}</p>
                        <p style=""margin:2px 0 0;font-family:'Lato',Arial,sans-serif;font-size:13px;color:{BrandMuted};line-height:1.5;"">{WebUtility.HtmlEncode(desc)}</p>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>";

    private static string GetFirstName(string email)
    {
        var local = email.Split('@')[0];
        var part = local.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ');
        if (string.IsNullOrWhiteSpace(part)) return "olá";
        var first = part.Split(' ')[0];
        return char.ToUpperInvariant(first[0]) + first[1..].ToLowerInvariant();
    }

    private static string GetFirstNameFromName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "olá";
        var first = fullName.Trim().Split(' ')[0];
        return char.ToUpperInvariant(first[0]) + first[1..].ToLowerInvariant();
    }
}
