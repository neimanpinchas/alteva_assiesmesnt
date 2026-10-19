namespace AltevaCrawler.Api.Security;

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using AltevaCrawler.Shared.Security;

public class AuthFilter : IAsyncActionFilter
{
    private readonly HmacAuthService _authService;

    public AuthFilter(HmacAuthService authService)
    {
        _authService = authService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Skip auth for health checks or token creation
        var path = context.HttpContext.Request.Path.Value?.ToLowerInvariant();
        if (path != null && (path.Contains("/health") || path.Contains("/api/auth/token")))
        {
            await next();
            return;
        }

        string? authHeader = context.HttpContext.Request.Headers["Authorization"];
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing or invalid Authorization Bearer token." });
            return;
        }

        string token = authHeader.Substring("Bearer ".Length).Trim();
        if (!_authService.VerifyToken(token, out _))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or expired session token." });
            return;
        }

        await next();
    }
}
