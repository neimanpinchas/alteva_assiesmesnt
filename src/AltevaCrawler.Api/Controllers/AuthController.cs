namespace AltevaCrawler.Api.Controllers;

using System;
using Microsoft.AspNetCore.Mvc;
using AltevaCrawler.Shared.Security;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly HmacAuthService _authService;

    public AuthController(HmacAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("token")]
    public IActionResult CreateToken()
    {
        string sessionId = Guid.NewGuid().ToString();
        string token = _authService.CreateToken(sessionId, TimeSpan.FromHours(8));
        return Ok(new { token, sessionId });
    }
}
