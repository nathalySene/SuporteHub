using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SuporteHub.API.DTOs;
using SuporteHub.API.Services;

namespace SuporteHub.API.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("password-reset")]
public sealed class PasswordResetController(PasswordResetService resets) : ControllerBase
{
    [HttpPost("forgot-password")]
    public IActionResult Forgot(ForgotPasswordDto dto)
    {
        if (!resets.IsConfigured) return StatusCode(503, new { mensagem = "A recuperação de senha está indisponível. Entre em contato com o suporte." });
        if (!resets.TryQueue(dto.Email)) return StatusCode(429, new { mensagem = "Aguarde alguns instantes antes de tentar novamente." });
        return Accepted(new { mensagem = "Se houver uma conta com esse e-mail, você receberá as instruções para redefinir a senha." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> Reset(ResetPasswordDto dto)
    {
        if (!await resets.Reset(dto.Email, dto.Token, dto.Senha))
            return BadRequest(new { mensagem = "Este link é inválido, expirou ou já foi utilizado. Solicite uma nova recuperação de senha." });
        return Ok(new { mensagem = "Senha atualizada. Entre novamente com sua nova senha." });
    }
}
