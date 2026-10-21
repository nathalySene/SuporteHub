using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using SuporteHub.API.Models;
using SuporteHub.API.Storage;

namespace SuporteHub.API.Services;

/// <summary>
/// Password-reset requests are acknowledged before account lookup. Account records contain
/// only a token hash; consuming that hash and changing the password is one conditional write.
/// </summary>
public sealed class PasswordResetService(
    IAppStore store,
    PasswordHasher<Conta> hasher,
    IAuthEmailSender sender,
    IConfiguration config,
    IWebHostEnvironment environment,
    ILogger<PasswordResetService> logger) : BackgroundService
{
    readonly Channel<string> requests = Channel.CreateBounded<string>(new BoundedChannelOptions(100)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });

    Uri? PublicBaseUri
    {
        get
        {
            if (!Uri.TryCreate(config["Authentication:PublicBaseUrl"], UriKind.Absolute, out var uri)
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || uri.AbsolutePath != "/") return null;
            if (uri.Scheme == Uri.UriSchemeHttps) return uri;
            return environment.IsDevelopment() && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp ? uri : null;
        }
    }

    public bool IsConfigured => sender.IsConfigured && PublicBaseUri is not null;
    public bool TryQueue(string email) => requests.Writer.TryWrite(email.Trim().ToLowerInvariant());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var email in requests.Reader.ReadAllAsync(stoppingToken))
            {
                try { await Deliver(email, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    // Do not log recipients, reset links, tokens or provider credential messages.
                    logger.LogWarning("Password-reset delivery failed ({FailureType}). Check storage and email configuration.", error.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    async Task Deliver(string email, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var record = await store.Get<Conta>("USERS", email);
            if (record is null) return;
            var account = record.Value.Value;
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var hash = Hash(token);
            account.PasswordResetTokenHash = hash;
            account.PasswordResetExpiresAt = DateTime.UtcNow.AddMinutes(30);
            try { await store.Put("USERS", email, account, record.Value.Version); }
            catch (StorageConflictException) { continue; }
            var link = new Uri(PublicBaseUri!, "/#/redefinir-senha?email=" + Uri.EscapeDataString(email) + "&token=" + Uri.EscapeDataString(token));
            try { await sender.SendPasswordReset(email, account.Nome, link, cancellationToken); }
            catch
            {
                // A failed send must not leave this attempted delivery's token active.
                await ClearFailedDelivery(email, hash);
                throw;
            }
            return;
        }
        logger.LogWarning("Password-reset request could not be saved after concurrent account updates.");
    }

    async Task ClearFailedDelivery(string email, string hash)
    {
        try
        {
            var record = await store.Get<Conta>("USERS", email);
            if (record is null || record.Value.Value.PasswordResetTokenHash != hash) return;
            var account = record.Value.Value;
            account.PasswordResetTokenHash = null; account.PasswordResetExpiresAt = null;
            await store.Put("USERS", email, account, record.Value.Version);
        }
        catch (Exception error)
        {
            logger.LogWarning("A failed reset delivery could not be cleared ({FailureType}); the token will expire automatically.", error.GetType().Name);
        }
    }

    public async Task<bool> Reset(string email, string token, string password)
    {
        email = email.Trim().ToLowerInvariant();
        var record = await store.Get<Conta>("USERS", email);
        var account = record?.Value;
        var validHash = Matches(account?.PasswordResetTokenHash, token);
        if (account is null || !validHash || account.PasswordResetExpiresAt is null || account.PasswordResetExpiresAt <= DateTime.UtcNow) return false;
        account.SenhaHash = hasher.HashPassword(account, password);
        account.PasswordResetTokenHash = null;
        account.PasswordResetExpiresAt = null;
        account.SecurityStamp = Guid.NewGuid().ToString("N");
        try { await store.Put("USERS", email, account, record!.Value.Version); }
        catch (StorageConflictException) { return false; }
        return true;
    }

    static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    static bool Matches(string? expectedHash, string token)
    {
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        byte[] expected;
        try { expected = expectedHash?.Length == 64 ? Convert.FromHexString(expectedHash) : new byte[32]; }
        catch (FormatException) { expected = new byte[32]; }
        return CryptographicOperations.FixedTimeEquals(expected, actual) && expectedHash is not null;
    }
}
