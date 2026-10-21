using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuporteHub.API.DTOs;
using SuporteHub.API.Models;
using SuporteHub.API.Storage;

namespace SuporteHub.API.Controllers;

[ApiController, Authorize, Route("api/chamados")]
public class ChamadosController(IAppStore store, IConfiguration config) : ControllerBase
{
    static readonly string[] Categories = ["Acesso", "Hardware", "Rede", "Sistemas", "Equipamentos"];
    static readonly string[] Priorities = ["Baixa", "Média", "Alta", "Urgente"];
    static readonly string[] Statuses = ["Aberto", "Em andamento", "Aguardando", "Resolvido"];
    string Email => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    string Name => User.Identity!.Name!;
    bool Agent => User.IsInRole("agent");
    static string Key(int id) => $"TICKET#{id:D10}";
    bool Allowed(Chamado t) => Agent || t.Usuario == Email;
    string Uploads => Path.GetFullPath(config["Storage:UploadsPath"] ?? ".local/uploads");

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var tickets = await store.Query<Chamado>("TICKETS");
        return Ok(tickets.Where(Allowed).OrderByDescending(t => t.DataAtualizacao));
    }
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var t = await store.Get<Chamado>("TICKETS", Key(id));
        return t is null || !Allowed(t.Value.Value) ? NotFound(new { mensagem = "Chamado não encontrado." }) : Ok(t.Value.Value);
    }
    [HttpPost]
    public async Task<IActionResult> Create(CriarChamadoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Titulo) || string.IsNullOrWhiteSpace(dto.Descricao) || !Categories.Contains(dto.Categoria) || !Priorities.Contains(dto.Prioridade))
            return BadRequest(new { mensagem = "Informe assunto, descrição, categoria e prioridade válidos." });
        var t = new Chamado
        {
            Id = await store.NextId(), Titulo = dto.Titulo.Trim(), Descricao = dto.Descricao.Trim(),
            Categoria = dto.Categoria, Prioridade = dto.Prioridade, Usuario = Email, NomeUsuario = Name,
            Historico = [new(Name, dto.Descricao.Trim(), "abertura", DateTime.UtcNow)]
        };
        await store.Put("TICKETS", Key(t.Id), t);
        return CreatedAtAction(nameof(Get), new { id = t.Id }, t);
    }

    async Task<IActionResult> Change(int id, Action<Chamado> change)
    {
        var entry = await store.Get<Chamado>("TICKETS", Key(id));
        if (entry is null || !Allowed(entry.Value.Value)) return NotFound(new { mensagem = "Chamado não encontrado." });
        var t = entry.Value.Value;
        try { change(t); }
        catch (ArgumentException e) { return BadRequest(new { mensagem = e.Message }); }
        t.DataAtualizacao = DateTime.UtcNow;
        try { await store.Put("TICKETS", Key(id), t, entry.Value.Version); }
        catch (StorageConflictException) { return Conflict(new { mensagem = "O chamado mudou durante o envio. Atualize a tela e tente novamente." }); }
        catch (InvalidOperationException e) { return BadRequest(new { mensagem = e.Message }); }
        return Ok(t);
    }

    [HttpPost("{id:int}/respostas")]
    public Task<IActionResult> Reply(int id, RespostaDto dto) => Change(id, t =>
    {
        if (string.IsNullOrWhiteSpace(dto.Texto)) throw new ArgumentException("Escreva sua resposta.");
        if (t.Status == "Resolvido") throw new ArgumentException("Reabra o chamado antes de responder.");
        t.Historico.Add(new(Name, dto.Texto.Trim(), "resposta", DateTime.UtcNow));
    });

    [HttpPatch("{id:int}/status")]
    public Task<IActionResult> Status(int id, StatusDto dto) => Change(id, t =>
    {
        if (!Statuses.Contains(dto.Status)) throw new ArgumentException("Status inválido.");
        if (!Agent && dto.Status != "Resolvido") throw new ArgumentException("Apenas agentes podem reabrir ou alterar a etapa do chamado.");
        if (t.Status == dto.Status) return;
        t.Status = dto.Status;
        t.Historico.Add(new(Name, "Status alterado para " + dto.Status + ".", "status", DateTime.UtcNow));
    });

    [Authorize(Roles = "agent"), HttpPatch("{id:int}/responsavel")]
    public async Task<IActionResult> Assign(int id, AtribuirDto dto)
    {
        Conta? agent = null;
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var entry = await store.Get<Conta>("USERS", dto.Email.Trim().ToLowerInvariant());
            if (entry is null || entry.Value.Value.Perfil != "agent") return BadRequest(new { mensagem = "Selecione um agente válido." });
            agent = entry.Value.Value;
        }
        return await Change(id, t =>
        {
            t.Responsavel = agent?.Email; t.NomeResponsavel = agent?.Nome;
            t.Historico.Add(new(Name, agent is null ? "Responsável removido." : "Chamado atribuído a " + agent.Nome + ".", "atribuicao", DateTime.UtcNow));
        });
    }

    [HttpPost("{id:int}/anexos"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Upload(int id, IFormFile arquivo)
    {
        var entry = await store.Get<Chamado>("TICKETS", Key(id));
        if (entry is null || !Allowed(entry.Value.Value)) return NotFound();
        if (arquivo.Length is <= 0 or > 10 * 1024 * 1024) return BadRequest(new { mensagem = "O arquivo deve ter até 10 MB." });
        var ext = Path.GetExtension(arquivo.FileName).ToLowerInvariant();
        var type = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".pdf" => "application/pdf", _ => null };
        if (type is null) return BadRequest(new { mensagem = "Envie arquivos PNG, JPG ou PDF." });
        await using var input = arquivo.OpenReadStream();
        var signature = new byte[8]; var read = await input.ReadAsync(signature);
        bool valid = read >= 5 && (ext == ".pdf" ? signature.AsSpan(0, 5).SequenceEqual("%PDF-"u8) : ext == ".png" ? read == 8 && signature.AsSpan().SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) : signature[0] == 255 && signature[1] == 216 && signature[2] == 255);
        if (!valid) return BadRequest(new { mensagem = "O conteúdo não corresponde ao tipo de arquivo informado." });
        Directory.CreateDirectory(Uploads);
        var fileId = Guid.NewGuid().ToString("N"); var path = Path.Combine(Uploads, fileId);
        try
        {
            await using (var output = System.IO.File.Create(path)) { await output.WriteAsync(signature.AsMemory(0, read)); await input.CopyToAsync(output); }
            var result = await Change(id, t =>
            {
                if (t.Status == "Resolvido") throw new ArgumentException("Reabra o chamado antes de anexar arquivos.");
                if (t.Anexos.Count >= 20) throw new ArgumentException("Limite de 20 anexos por chamado.");
                t.Anexos.Add(new(fileId, Path.GetFileName(arquivo.FileName)[..Math.Min(Path.GetFileName(arquivo.FileName).Length, 200)], type, arquivo.Length, Name, DateTime.UtcNow));
                t.Historico.Add(new(Name, "Anexo adicionado: " + Path.GetFileName(arquivo.FileName), "anexo", DateTime.UtcNow));
            });
            if (result is not OkObjectResult) System.IO.File.Delete(path);
            return result;
        }
        catch { System.IO.File.Delete(path); throw; }
    }

    [HttpGet("{id:int}/anexos/{fileId}")]
    public async Task<IActionResult> Download(int id, string fileId)
    {
        var entry = await store.Get<Chamado>("TICKETS", Key(id));
        if (entry is null || !Allowed(entry.Value.Value)) return NotFound();
        var f = entry.Value.Value.Anexos.FirstOrDefault(a => a.Id == fileId);
        if (f is null) return NotFound();
        var path = Path.Combine(Uploads, f.Id);
        if (!System.IO.File.Exists(path)) return NotFound(new { mensagem = "Arquivo indisponível no armazenamento." });
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, f.ContentType, f.Nome);
    }
}
