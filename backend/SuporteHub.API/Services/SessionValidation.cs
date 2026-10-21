using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SuporteHub.API.Models;
using SuporteHub.API.Storage;

namespace SuporteHub.API.Services;

public static class SessionValidation
{
    public static async Task Validate(CookieValidatePrincipalContext context)
    {
        var email = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var stamp = context.Principal?.FindFirstValue(GoogleAuthConfiguration.SecurityStampClaim) ?? "";
        var store = context.HttpContext.RequestServices.GetRequiredService<IAppStore>();
        var account = string.IsNullOrEmpty(email) ? null : await store.Get<Conta>("USERS", email);
        if (account is null || !StringComparer.Ordinal.Equals(account.Value.Value.SecurityStamp ?? "", stamp))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
