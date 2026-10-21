using System.Net;
using System.Net.Mail;
using System.Text;

namespace SuporteHub.API.Services;

public interface IAuthEmailSender
{
    bool IsConfigured { get; }
    bool IsLocalDelivery { get; }
    Task SendPasswordReset(string email, string name, Uri link, CancellationToken cancellationToken);
}

/// <summary>SMTP delivery with verified STARTTLS, or an explicit development-only local outbox.</summary>
public sealed class AuthEmailSender(IConfiguration config, IWebHostEnvironment environment) : IAuthEmailSender
{
    string Mode => config["Email:DeliveryMode"] ?? (environment.IsDevelopment() ? "File" : "Disabled");
    bool FileMode => Mode.Equals("File", StringComparison.OrdinalIgnoreCase) && environment.IsDevelopment();
    public bool IsLocalDelivery => FileMode;
    int Port => int.TryParse(config["Email:Smtp:Port"], out var port) ? port : 587;

    public bool IsConfigured
    {
        get
        {
            if (FileMode) return !string.IsNullOrWhiteSpace(config["Email:OutboxPath"]);
            if (!Mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.IsNullOrWhiteSpace(config["Email:Smtp:Host"]) || Port is <= 0 or > 65535 || Port == 465) return false;
            var user = config["Email:Smtp:UserName"];
            var password = config["Email:Smtp:Password"];
            if (string.IsNullOrWhiteSpace(user) != string.IsNullOrWhiteSpace(password)) return false;
            return MailAddress.TryCreate(config["Email:Smtp:FromAddress"], out _);
        }
    }

    public async Task SendPasswordReset(string email, string name, Uri link, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new InvalidOperationException("O envio de e-mail não está configurado.");
        var from = FileMode ? "no-reply@suportehub.local" : config["Email:Smtp:FromAddress"]!;
        var fromName = config["Email:Smtp:FromName"] ?? "SuporteHub";
        using var message = new MailMessage(new MailAddress(from, fromName), new MailAddress(email))
        {
            Subject = "Redefina sua senha do SuporteHub",
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
            Body = $"Olá, {name}.\n\nRecebemos uma solicitação para redefinir sua senha.\n\nAbra este endereço para escolher uma nova senha:\n{link.AbsoluteUri}\n\nO link expira em 30 minutos e pode ser usado uma única vez.\nSe você não fez esta solicitação, ignore esta mensagem.\n\nEquipe SuporteHub"
        };
        using var client = new SmtpClient { Timeout = 15000 };
        if (FileMode)
        {
            var outbox = Path.GetFullPath(config["Email:OutboxPath"]!);
            Directory.CreateDirectory(outbox);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(outbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = outbox;
        }
        else
        {
            client.Host = config["Email:Smtp:Host"]!;
            client.Port = Port;
            client.EnableSsl = true; // Require STARTTLS and normal platform certificate verification.
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            var user = config["Email:Smtp:UserName"];
            if (!string.IsNullOrWhiteSpace(user)) client.Credentials = new NetworkCredential(user, config["Email:Smtp:Password"]);
        }
        await client.SendMailAsync(message, cancellationToken);
    }
}
