using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SuporteHub.API.DTOs;
using SuporteHub.API.Models;
using SuporteHub.API.Storage;

namespace SuporteHub.API.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(IAppStore store, PasswordHasher<Conta> hasher) : ControllerBase
{
    public static object Public(Conta c) => new { c.Email, c.Nome, c.Perfil };
    async Task SignIn(Conta c, bool remember = false)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, c.Email), new Claim(ClaimTypes.Name, c.Nome), new Claim(ClaimTypes.Role, c.Perfil) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = remember });
    }

    [HttpGet("setup")]
    public async Task<IActionResult> Setup() => Ok(new { primeiroAcesso = await store.NeedsInitialAgent() });

    [HttpPost("cadastro"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(CadastroDto dto)
    {
        var c = new Conta { Email = dto.Email.Trim().ToLowerInvariant(), Nome = dto.Nome.Trim() };
        if (c.Nome.Length < 2) return BadRequest(new { mensagem = "Informe seu nome." });
        c.SenhaHash = hasher.HashPassword(c, dto.Senha);
        try { await store.Register(c); }
        catch (StorageConflictException) { return Conflict(new { mensagem = "Não foi possível cadastrar este e-mail. Tente entrar na sua conta." }); }
        await SignIn(c);
        return StatusCode(201, Public(c));
    }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var record = await store.Get<Conta>("USERS", dto.Email.Trim().ToLowerInvariant());
        var c = record?.Value ?? new Conta();
        // Always perform password hashing to avoid a fast path for unknown accounts.
        var hash = record is null ? hasher.HashPassword(c, "unavailable-account") : c.SenhaHash;
        var result = hasher.VerifyHashedPassword(c, hash, dto.Senha);
        if (record is null || c.Perfil is not ("admin" or "user") || result == PasswordVerificationResult.Failed) return Unauthorized(new { mensagem = "E-mail ou senha inválidos." });
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            c.SenhaHash = hasher.HashPassword(c, dto.Senha);
            try { await store.Put("USERS", c.Email, c, record.Value.Version); } catch (StorageConflictException) { }
        }
        await SignIn(c, dto.Lembrar);
        return Ok(Public(c));
    }

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var c = await store.Get<Conta>("USERS", User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return c is null ? Unauthorized() : Ok(Public(c.Value.Value));
    }
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await HttpContext.SignOutAsync(); return NoContent(); }
}
