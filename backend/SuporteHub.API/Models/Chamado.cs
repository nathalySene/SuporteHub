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
    public DateTime? DataResolucao { get; set; }
    public string? ResolvidoPor { get; set; }
    public string? NomeResolvidoPor { get; set; }
    public List<Historico> NotasInternas { get; set; } = [];
    public List<Historico> Historico { get; set; } = [];
    public List<Anexo> Anexos { get; set; } = [];
}

public record Historico(string Autor, string Texto, string Tipo, DateTime Data, string? AutorEmail = null, string? PerfilAutor = null);
public record Anexo(string Id, string Nome, string ContentType, long Tamanho, string Autor, DateTime Data);
public class Conta
{
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    string perfil = "user";
    // Read legacy accounts without rewriting the database or changing passwords.
    public string Perfil
    {
        get => perfil switch { "agent" => "admin", "client" => "user", _ => perfil };
        set => perfil = value;
    }
    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;
}
