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
        var store = context.HttpContext.RequestServices.GetRequiredService<IAppStore>();
        var entry = email is null ? null : await store.Get<Conta>("USERS", email);
        // Legacy role cookies must log in again; never silently elevate an old session.
        if (entry is null || entry.Value.Value.Perfil is not ("admin" or "user")
            || entry.Value.Value.Perfil != context.Principal?.FindFirstValue(ClaimTypes.Role))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
