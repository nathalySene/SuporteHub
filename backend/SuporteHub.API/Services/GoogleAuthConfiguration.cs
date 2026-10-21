using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace SuporteHub.API.Services;

public static class GoogleAuthConfiguration
{
    public const string Scheme = "Google";
    public const string ExternalScheme = "GoogleExternal";
    public const string VerifiedEmailClaim = "suportehub:google-email-verified";
    public const string SecurityStampClaim = "suportehub:security-stamp";

    public static bool IsConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientId"])
        && !string.IsNullOrWhiteSpace(config["Authentication:Google:ClientSecret"]);

    // Google v2 userinfo uses id/verified_email; OIDC userinfo uses sub/email_verified.
    // Accept only a boolean true from the authenticated userinfo response.
    public static Task NormalizeClaims(OAuthCreatingTicketContext context)
    {
        var identity = context.Identity!;
        var json = context.User;
        foreach (var claim in identity.FindAll(VerifiedEmailClaim).ToList()) identity.RemoveClaim(claim);
        bool verified = (json.TryGetProperty("verified_email", out var legacy) && legacy.ValueKind == JsonValueKind.True)
            || (json.TryGetProperty("email_verified", out var standard) && standard.ValueKind == JsonValueKind.True);
        if (verified) identity.AddClaim(new Claim(VerifiedEmailClaim, "true"));
        string? subject = ReadString(json, "sub") ?? ReadString(json, "id");
        if (!string.IsNullOrWhiteSpace(subject))
        {
            foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).ToList()) identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        }
        return Task.CompletedTask;
    }

    static string? ReadString(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
