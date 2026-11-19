
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

    // REGISTER COMPANY ADMIN
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequestDto dto)
    {
        var result = await _authService.RegisterAsync(dto);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        return Ok(new
        {
            message = "Account created successfully."
        });
    }

    // LOGIN
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto dto)
    {
        var result = await _authService.LoginAsync(dto);

        if (!result.Succeeded)
            return Unauthorized(new { errors = result.Errors });

        return Ok(new { token = result.Token });
    }

    // GET COMPANY TEAM MEMBERS
    [HttpGet("team-members")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> GetTeamMembers()
    {
        var companyId = GetCompanyId();

        if (companyId == null)
            return Forbid();

        var members = await _authService.GetTeamMembersAsync(
            companyId.Value);

        return Ok(members);
    }

    // CREATE TEAM MEMBER
    [HttpPost("team-members")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> CreateTeamMember(
        [FromBody] CreateTeamMemberDto dto)
    {
        var companyId = GetCompanyId();

        if (companyId == null)
            return Forbid();

        var result = await _authService.CreateTeamMemberAsync(
            dto,
            companyId.Value);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        return StatusCode(201, new
        {
            message = "Team member created successfully."
        });
    }

    // UPDATE TEAM MEMBER
    [HttpPut("team-members/{userId}")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> UpdateTeamMember(
        string userId,
        [FromBody] UpdateTeamMemberDto dto)
    {
        var companyId = GetCompanyId();

        if (companyId == null)
            return Forbid();

        var result = await _authService.UpdateTeamMemberAsync(
            userId,
            dto,
            companyId.Value);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        return Ok(new
        {
            message = "Team member updated successfully."
        });
    }

    // DEACTIVATE TEAM MEMBER
    [HttpPatch("team-members/{userId}/deactivate")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> DeactivateTeamMember(
        string userId)
    {
        var companyId = GetCompanyId();

        if (companyId == null)
            return Forbid();

        var result = await _authService.DeactivateTeamMemberAsync(
            userId,
            companyId.Value);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors });

        return Ok(new
        {
            message = "Team member deactivated successfully."
        });
    }

    // GET COMPANY ID FROM JWT
    private int? GetCompanyId()
    {
        var companyIdValue =
            User.FindFirst("company_id")?.Value;

        if (!int.TryParse(companyIdValue, out var companyId)
            || companyId <= 0)
        {
            return null;
        }

        return companyId;
    }
}
