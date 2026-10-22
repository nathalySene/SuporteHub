using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SuporteHub.API.Models;
using SuporteHub.API.Storage;
namespace SuporteHub.API.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/equipe")]
public class EquipeController(IAppStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok((await store.Query<Conta>("USERS"))
        .Where(c => c.Perfil == "admin").Select(AuthController.Public));
}
