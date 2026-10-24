using ConstructionSaaS.Application.DTOs.Auth;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace ConstructionSaaS.Infrastructure.Identity;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly RoleManager<IdentityRole> _roleManager;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        RoleManager<IdentityRole> roleManager   )
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _roleManager = roleManager;
    }

    public async Task<AuthResultDto> RegisterAsync(RegisterRequestDto dto)
    {
        var email = dto.Email.Trim();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email
        };

        var result = await _userManager.CreateAsync(user, dto.Password);

        if (!result.Succeeded)
        {
            return new AuthResultDto
            {
                Succeeded = false,
                Errors = result.Errors
                    .Select(error => error.Description)
                    .ToList()
            };
        }

        var roleResult = await _userManager.AddToRoleAsync(user, "CompanyAdmin");

        return new AuthResultDto
        {
            Succeeded = roleResult.Succeeded,
            Errors = roleResult.Errors
                .Select(error => error.Description)
                .ToList()
        };
    }

    public async Task<LoginResultDto> LoginAsync(LoginRequestDto dto)
    {
        var user = await _userManager.FindByEmailAsync(dto.Email.Trim());

        if (user == null ||
            !await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            return new LoginResultDto
            {
                Succeeded = false,
                Errors = new List<string> { "Invalid email or password." }
            };
        }

        var roles = await _userManager.GetRolesAsync(user);

        var token = _tokenService.CreateToken(
            user.Id,
            user.Email!,
            roles,
            user.CompanyId);

        return new LoginResultDto
        {
            Succeeded = true,
            Token = token
        };
    }

    public async Task<AuthResultDto> CreateTeamMemberAsync(
        CreateTeamMemberDto dto,
        int companyId)
    {
        if (companyId <= 0)
        {
            return new AuthResultDto
            {
                Succeeded = false,
                Errors = new List<string> { "Valid company ID is required." }
            };
        }

        var requestedRole = dto.Role.Trim();

        var role = new[] { "ProjectManager", "SiteEngineer" }
            .FirstOrDefault(allowedRole =>
                string.Equals(
                    allowedRole,
                    requestedRole,
                    StringComparison.OrdinalIgnoreCase));

        if (role == null)
        {
            return new AuthResultDto
            {
                Succeeded = false,
                Errors = new List<string>
                {
                    "Role must be ProjectManager or SiteEngineer."
                }
            };
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            return new AuthResultDto
            {
                Succeeded = false,
                Errors = new List<string> { $"Role '{role}' does not exist." }
            };
        }

        var email = dto.Email.Trim().ToLowerInvariant();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            CompanyId = companyId
        };

        var createResult = await _userManager.CreateAsync(user, dto.Password);

        if (!createResult.Succeeded)
        {
            return new AuthResultDto
            {
                Succeeded = false,
                Errors = createResult.Errors
                    .Select(error => error.Description)
                    .ToList()
            };
        }

        var roleResult = await _userManager.AddToRoleAsync(user, role);

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);

            return new AuthResultDto
            {
                Succeeded = false,
                Errors = roleResult.Errors
                    .Select(error => error.Description)
                    .ToList()
            };
        }

        return new AuthResultDto
        {
            Succeeded = true,
            Errors = new List<string>()
        };
    }
}