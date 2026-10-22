using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SuporteHub.API.DTOs;
using SuporteHub.API.Models;
using SuporteHub.API.Storage;

namespace SuporteHub.API.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/admin")]
public class AdminController(IAppStore store, PasswordHasher<Conta> hasher) : ControllerBase
{
    [HttpGet("usuarios")]
    public async Task<IActionResult> Users() => Ok((await store.Query<Conta>("USERS")).Select(AuthController.Public));

    [HttpGet("solicitantes/{email}")]
    public async Task<IActionResult> Requester(string email)
    {
        var entry = await store.Get<Conta>("USERS", email.Trim().ToLowerInvariant());
        if (entry is null) return NotFound(new { mensagem = "Solicitante não encontrado." });
        var c = entry.Value.Value;
        var tickets = (await store.Query<Chamado>("TICKETS")).Where(t => t.Usuario == c.Email).ToList();
        return Ok(new { c.Nome, c.Email, c.DataCriacao, chamados = tickets.Count, resolvidos = tickets.Count(t => t.Status == "Resolvido") });
    }

    [HttpPost("usuarios"), EnableRateLimiting("auth")]
    public async Task<IActionResult> AddUser(CadastroAdministrativoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Nome) || dto.Nome.Trim().Length < 2) return BadRequest(new { mensagem = "Informe o nome." });
        if (dto.Perfil is not ("admin" or "user")) return BadRequest(new { mensagem = "Escolha Administrador ou Usuário." });
        var c = new Conta { Email = dto.Email.Trim().ToLowerInvariant(), Nome = dto.Nome.Trim(), Perfil = dto.Perfil };
        c.SenhaHash = hasher.HashPassword(c, dto.Senha);
        try { await store.Put("USERS", c.Email, c); }
        catch (StorageConflictException) { return Conflict(new { mensagem = "Já existe uma conta com esse e-mail." }); }
        return StatusCode(201, AuthController.Public(c));
    }

    [HttpPost("chamados")]
    public async Task<IActionResult> Create(AdminChamadoDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Titulo) || string.IsNullOrWhiteSpace(dto.Descricao)
            || !new[] { "Acesso", "Hardware", "Rede", "Sistemas", "Equipamentos" }.Contains(dto.Categoria)
            || !new[] { "Baixa", "Média", "Alta", "Urgente" }.Contains(dto.Prioridade))
            return BadRequest(new { mensagem = "Informe assunto, descrição, categoria e prioridade válidos." });
        var entry = await store.Get<Conta>("USERS", dto.Solicitante.Trim().ToLowerInvariant());
        if (entry is null) return BadRequest(new { mensagem = "Selecione um solicitante cadastrado." });
        var c = entry.Value.Value;
        var t = new Chamado {
            Id = await store.NextId(), Titulo = dto.Titulo.Trim(), Descricao = dto.Descricao.Trim(),
            Categoria = dto.Categoria, Prioridade = dto.Prioridade, Usuario = c.Email, NomeUsuario = c.Nome,
            Historico = [new(User.Identity!.Name!, "Chamado aberto em nome de " + c.Nome + ".", "abertura", DateTime.UtcNow, User.FindFirstValue(ClaimTypes.NameIdentifier), "admin")]
        };
        await store.Put("TICKETS", $"TICKET#{t.Id:D10}", t);
        return StatusCode(201, t);
    }
}
