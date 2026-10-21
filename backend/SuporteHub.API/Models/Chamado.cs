namespace SuporteHub.API.Models;

public class Chamado
{
    public int Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public string Categoria { get; set; } = "";
    public string Prioridade { get; set; } = "Média";
    public string Usuario { get; set; } = "";
    public string NomeUsuario { get; set; } = "";
    public string Status { get; set; } = "Aberto";
    public string? Responsavel { get; set; }
    public string? NomeResponsavel { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
    public DateTime DataAtualizacao { get; set; } = DateTime.UtcNow;
    public List<Historico> Historico { get; set; } = [];
    public List<Anexo> Anexos { get; set; } = [];
}

public record Historico(string Autor, string Texto, string Tipo, DateTime Data);
public record Anexo(string Id, string Nome, string ContentType, long Tamanho, string Autor, DateTime Data);
public class Conta
{
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public string Perfil { get; set; } = "client";
    public string? GoogleSubject { get; set; }
    // Empty preserves existing accounts/cookies; credential changes replace this stamp.
    public string SecurityStamp { get; set; } = "";
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresAt { get; set; }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
}
