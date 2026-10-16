using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Auth;
using SupportDesk.Application.Features.Auth.Commands.Login;

namespace SupportDesk.Presentation.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>Logs in an agent using email and password, returning a signed JWT access token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        [FromServices] LoginCommandHandler handler,
        CancellationToken ct)
    {
        var response = await handler.HandleAsync(request, ct);
        return Ok(response);
    }

    /// <summary>Returns the authenticated agent's profile based on the validated JWT claims.</summary>
    [HttpGet("me")]
    public ActionResult<CurrentUserDto> Me([FromServices] ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.AgentId is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Detail = "Authentication is required to access this resource.",
                Status = StatusCodes.Status401Unauthorized
            });
        }

        return Ok(new CurrentUserDto
        {
            AgentId = currentUser.AgentId.Value,
            Name = currentUser.FullName ?? string.Empty,
            Email = currentUser.Email ?? string.Empty
        });
    }
}
