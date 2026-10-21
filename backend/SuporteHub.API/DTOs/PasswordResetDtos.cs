using System.ComponentModel.DataAnnotations;

namespace SuporteHub.API.DTOs;

public record ForgotPasswordDto([Required, EmailAddress, StringLength(254)] string Email);
public record ResetPasswordDto(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 32)] string Token,
    [Required, StringLength(128, MinimumLength = 10)] string Senha);
