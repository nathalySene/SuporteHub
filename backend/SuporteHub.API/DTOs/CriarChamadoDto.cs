using System.ComponentModel.DataAnnotations;
namespace SuporteHub.API.DTOs;

public record CriarChamadoDto(
    [Required, StringLength(120, MinimumLength = 3)] string Titulo,
    [Required, StringLength(10000, MinimumLength = 3)] string Descricao,
    [Required] string Categoria,
    [Required] string Prioridade);
public record CadastroDto(
    [Required, StringLength(100, MinimumLength = 2)] string Nome,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Senha);
public record LoginDto([Required, EmailAddress] string Email, [Required, StringLength(128)] string Senha, bool Lembrar = false);
public record RespostaDto([Required, StringLength(10000, MinimumLength = 1)] string Texto);
public record StatusDto([Required] string Status);
public record AtribuirDto(string? Email);

public record GerenciarDto([Required] string Status, [Required] string Prioridade, string? Responsavel);
public record AdminChamadoDto(
    [Required, StringLength(120, MinimumLength = 3)] string Titulo,
    [Required, StringLength(10000, MinimumLength = 3)] string Descricao,
    [Required] string Categoria, [Required] string Prioridade,
    [Required, EmailAddress, StringLength(254)] string Solicitante);

public record CadastroAdministrativoDto(
    [Required, StringLength(100, MinimumLength = 2)] string Nome,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 10)] string Senha,
    [Required] string Perfil);
