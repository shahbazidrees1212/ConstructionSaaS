using ConstructionSaaS.Application.DTOs.Auth;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto dto)
    {
        var result = await _authService.RegisterAsync(dto);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        return Ok(new { message = "Account created successfully." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto dto)
    {
        var result = await _authService.LoginAsync(dto);

        if (!result.Succeeded)
            return Unauthorized(new { errors = result.Errors });

        return Ok(new { token = result.Token });
    }
    [HttpPost("team-members")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> CreateTeamMember(
    [FromBody] CreateTeamMemberDto dto)
    {
        var companyIdValue = User.FindFirst("company_id")?.Value;

        if (!int.TryParse(companyIdValue, out var companyId))
            return Forbid();

        var result = await _authService.CreateTeamMemberAsync(dto, companyId);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        return StatusCode(201, new
        {
            message = "Team member created successfully."
        });
    }
}