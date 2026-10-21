using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SuporteHub.API.Models;
using SuporteHub.API.Services;
using SuporteHub.API.Storage;

namespace SuporteHub.API.Controllers;

[ApiController, Route("api/auth/google")]
public class GoogleAuthController(IAppStore store, IConfiguration config, IDataProtectionProvider protection) : ControllerBase
{
    const string PendingCookie = "SuporteHub.GoogleLink";
    const string IntentKey = "suportehub:google-intent";
    const string ActorKey = "suportehub:google-actor";
    const string StampKey = "suportehub:google-actor-stamp";
    const string StartedKey = "suportehub:google-started";
    const string PendingPath = "/api/auth/google/start";
    readonly IDataProtector linkProtector = protection.CreateProtector("SuporteHub.GoogleLink.v1");
    record PendingLink(string Email, string Stamp, long CreatedAt);

    CookieOptions PendingOptions => new()
    {
        HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict,
        Path = PendingPath, MaxAge = TimeSpan.FromMinutes(5), Expires = DateTimeOffset.UtcNow.AddMinutes(5)
    };

    [HttpGet("start"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Start([FromQuery] string? link = null)
    {
        if (!GoogleAuthConfiguration.IsConfigured(config)) return Failure("google-unavailable", link == "1");
        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/google/finish" };
        properties.Items[IntentKey] = "login";
        properties.Items[StartedKey] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        if (link is not null)
        {
            if (link != "1" || User.Identity?.IsAuthenticated != true) return Failure("google-invalid", true);
            PendingLink? pending = null;
            if (Request.Cookies.TryGetValue(PendingCookie, out var cookie))
            {
                try { pending = JsonSerializer.Deserialize<PendingLink>(linkProtector.Unprotect(cookie)); }
                catch (Exception e) when (e is CryptographicException or JsonException) { }
            }
            Response.Cookies.Delete(PendingCookie, new CookieOptions { Path = PendingPath });
            if (pending is null || pending.Email != User.FindFirstValue(ClaimTypes.NameIdentifier)
                || pending.Stamp != (User.FindFirstValue(GoogleAuthConfiguration.SecurityStampClaim) ?? "")
                || !Fresh(pending.CreatedAt, 5)) return Failure("google-invalid", true);
            var account = await store.Get<Conta>("USERS", pending.Email);
            if (account is null || (account.Value.Value.SecurityStamp ?? "") != pending.Stamp) return Failure("google-invalid", true);
            properties.Items[IntentKey] = "link";
            properties.Items[ActorKey] = pending.Email;
            properties.Items[StampKey] = pending.Stamp;
        }
        return Challenge(properties, GoogleAuthConfiguration.Scheme);
    }

    // A same-origin, authenticated POST initiates linking. No bearer/link token is put in a URL.
    [Authorize, HttpPost("link"), EnableRateLimiting("auth")]
    public async Task<IActionResult> PrepareLink()
    {
        if (!GoogleAuthConfiguration.IsConfigured(config))
            return StatusCode(503, new { mensagem = "A conexão com o Google ainda não está disponível." });
        var email = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var entry = await store.Get<Conta>("USERS", email);
        if (entry is null) return Unauthorized();
        if (!string.IsNullOrEmpty(entry.Value.Value.GoogleSubject))
            return Conflict(new { mensagem = "Sua conta já está conectada ao Google." });
        var stamp = entry.Value.Value.SecurityStamp ?? "";
        if (stamp != (User.FindFirstValue(GoogleAuthConfiguration.SecurityStampClaim) ?? "")) return Unauthorized();
        var pending = new PendingLink(email, stamp, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Response.Cookies.Append(PendingCookie, linkProtector.Protect(JsonSerializer.Serialize(pending)), PendingOptions);
        return Ok(new { url = "/api/auth/google/start?link=1" });
    }

    [HttpGet("finish")]
    public async Task<IActionResult> Finish()
    {
        if (!GoogleAuthConfiguration.IsConfigured(config)) return Failure("google-unavailable");
        var external = await HttpContext.AuthenticateAsync(GoogleAuthConfiguration.ExternalScheme);
        // Remove the temporary identity before issuing any final application session.
        await HttpContext.SignOutAsync(GoogleAuthConfiguration.ExternalScheme);
        var linking = Lookup(external.Properties?.Items, IntentKey) == "link";
        if (!external.Succeeded || external.Principal is null || external.Properties is null)
            return Failure("google-invalid", linking);
        if (!long.TryParse(Lookup(external.Properties.Items, StartedKey), NumberStyles.None, CultureInfo.InvariantCulture, out var started)
            || !Fresh(started, 10)) return Failure("google-invalid", linking);
        var principal = external.Principal;
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        if (principal.FindFirstValue(GoogleAuthConfiguration.VerifiedEmailClaim) != "true"
            || string.IsNullOrWhiteSpace(subject) || subject.Length > 255 || string.IsNullOrEmpty(email)
            || email.Length > 254 || !new EmailAddressAttribute().IsValid(email)) return Failure("google-invalid", linking);
        var bound = (await store.Query<Conta>("USERS")).Where(c => c.GoogleSubject == subject).ToList();
        if (bound.Count > 1) return Failure("google-conflict", linking);

        if (linking)
        {
            var actor = Lookup(external.Properties.Items, ActorKey);
            var stamp = Lookup(external.Properties.Items, StampKey);
            if (string.IsNullOrEmpty(actor) || stamp is null) return Failure("google-invalid", true);
            var entry = await store.Get<Conta>("USERS", actor);
            if (entry is null || (entry.Value.Value.SecurityStamp ?? "") != stamp) return Failure("google-invalid", true);
            var account = entry.Value.Value;
            if (email != account.Email) return Failure("google-email-mismatch", true);
            if ((bound.Count == 1 && bound[0].Email != actor)
                || (!string.IsNullOrEmpty(account.GoogleSubject) && account.GoogleSubject != subject))
                return Failure("google-already-linked", true);
            if (account.GoogleSubject != subject)
            {
                account.GoogleSubject = subject;
                try { await store.Put("USERS", account.Email, account, entry.Value.Version); }
                catch (StorageConflictException) { return Failure("google-conflict", true); }
            }
            await SignIn(account);
            return Redirect("/#/conta?google=linked");
        }

        // A stable provider subject wins over email, including a later verified Google email rename.
        if (bound.Count == 1)
        {
            await SignIn(bound[0]);
            return Destination(bound[0]);
        }
        var existing = await store.Get<Conta>("USERS", email);
        if (existing is not null) return Failure("google-account-exists");
        if (await store.NeedsInitialAgent()) return Failure("google-setup-required");
        var name = principal.FindFirstValue(ClaimTypes.Name)?.Trim();
        var created = new Conta
        {
            Email = email, Nome = string.IsNullOrWhiteSpace(name) ? email : name[..Math.Min(name.Length, 100)],
            GoogleSubject = subject, Perfil = "client", SenhaHash = "", SecurityStamp = Guid.NewGuid().ToString("N")
        };
        // Social registration never claims the initial support role.
        try { await store.Put("USERS", created.Email, created); }
        catch (StorageConflictException)
        {
            var concurrent = await store.Get<Conta>("USERS", email);
            if (concurrent is null || concurrent.Value.Value.GoogleSubject != subject) return Failure("google-conflict");
            created = concurrent.Value.Value;
        }
        await SignIn(created);
        return Destination(created);
    }

    static string? Lookup(IDictionary<string, string?>? items, string key) =>
        items is not null && items.TryGetValue(key, out var value) ? value : null;

    static bool Fresh(long started, int minutes)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return started >= now - minutes * 60 && started <= now + 30;
    }
    IActionResult Failure(string code, bool linking = false) => Redirect($"/#/{(linking ? "conta" : "login")}?erro={code}");
    IActionResult Destination(Conta account) => Redirect(account.Perfil == "agent" ? "/#/painel" : "/#/novo");
    Task SignIn(Conta account)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Email), new Claim(ClaimTypes.Name, account.Nome),
            new Claim(ClaimTypes.Role, account.Perfil),
            new Claim(GoogleAuthConfiguration.SecurityStampClaim, account.SecurityStamp ?? "")
        };
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
    }
}
