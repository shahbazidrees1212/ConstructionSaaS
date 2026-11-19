
using ConstructionSaaS.Application.DTOs.Auth;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Identity;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ApplicationDbContext _context;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        RoleManager<IdentityRole> roleManager,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _roleManager = roleManager;
        _context = context;
    }

    private static AuthResultDto Failure(params string[] errors) => new()
    {
        Succeeded = false,
        Errors = errors.ToList()
    };

    private static AuthResultDto Success() => new()
    {
        Succeeded = true,
        Errors = new List<string>()
    };

    private static AuthResultDto IdentityFailure(IdentityResult result) => new()
    {
        Succeeded = false,
        Errors = result.Errors.Select(x => x.Description).ToList()
    };

    private static string? NormalizeTeamRole(string? role)
    {
        return new[] { "ProjectManager", "SiteEngineer" }
            .FirstOrDefault(x => string.Equals(
                x,
                role?.Trim(),
                StringComparison.OrdinalIgnoreCase));
    }

    // COMPANY REGISTRATION
    public async Task<AuthResultDto> RegisterAsync(RegisterRequestDto dto)
    {
        if (dto == null ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password) ||
            string.IsNullOrWhiteSpace(dto.CompanyName) ||
            string.IsNullOrWhiteSpace(dto.Country))
        {
            return Failure("All registration fields are required.");
        }

        if (dto.Password != dto.ConfirmPassword)
            return Failure("Passwords do not match.");

        var email = dto.Email.Trim().ToLowerInvariant();

        if (await _userManager.FindByEmailAsync(email) != null)
            return Failure("Email is already registered.");

        if (!await _roleManager.RoleExistsAsync("CompanyAdmin"))
            return Failure("CompanyAdmin role does not exist.");

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var company = new Company
            {
                Name = dto.CompanyName.Trim(),
                Email = email,
                Country = dto.Country.Trim()
            };

            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                CompanyId = company.Id,
                IsActive = true
            };

            var createResult =
                await _userManager.CreateAsync(user, dto.Password);

            if (!createResult.Succeeded)
                return IdentityFailure(createResult);

            var roleResult =
                await _userManager.AddToRoleAsync(user, "CompanyAdmin");

            if (!roleResult.Succeeded)
                return IdentityFailure(roleResult);

            await transaction.CommitAsync();

            return Success();
        });
    }

    // LOGIN
    public async Task<LoginResultDto> LoginAsync(LoginRequestDto dto)
    {
        LoginResultDto LoginFailure(string message) => new()
        {
            Succeeded = false,
            Errors = new List<string> { message }
        };

        if (dto == null ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password))
        {
            return LoginFailure("Email and password are required.");
        }

        var user = await _userManager.FindByEmailAsync(
            dto.Email.Trim());

        if (user == null)
            return LoginFailure("Invalid email or password.");

        if (!user.IsActive)
            return LoginFailure("Invalid email or password.");

        if (await _userManager.IsLockedOutAsync(user))
            return LoginFailure("Invalid email or password.");

        if (user.CompanyId.HasValue && !await _context.Companies
            .AnyAsync(c => c.Id == user.CompanyId.Value && c.IsActive))
            return LoginFailure("Invalid email or password.");

        if (!await _userManager.CheckPasswordAsync(user, dto.Password))
        {
            await _userManager.AccessFailedAsync(user);
            return LoginFailure("Invalid email or password.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);

        var token = _tokenService.CreateToken(
            user.Id,
            user.Email!,
            roles,
            user.CompanyId, user.SecurityStamp);

        return new LoginResultDto
        {
            Succeeded = true,
            Token = token
        };
    }

    // CREATE TEAM MEMBER
    public async Task<AuthResultDto> CreateTeamMemberAsync(
        CreateTeamMemberDto dto,
        int companyId)
    {
        if (companyId <= 0)
            return Failure("Valid company ID is required.");

        if (dto == null ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password) ||
            string.IsNullOrWhiteSpace(dto.Role))
        {
            return Failure("Email, password and role are required.");
        }

        var role = NormalizeTeamRole(dto.Role);

        if (role == null ||
            !await _roleManager.RoleExistsAsync(role))
        {
            return Failure("Role must be ProjectManager or SiteEngineer.");
        }

        if (!await _context.Companies
            .AnyAsync(x => x.Id == companyId))
        {
            return Failure("Company does not exist.");
        }

        var email = dto.Email.Trim().ToLowerInvariant();

        if (await _userManager.FindByEmailAsync(email) != null)
            return Failure("Email is already registered.");

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                CompanyId = companyId,
                IsActive = true
            };

            var createResult =
                await _userManager.CreateAsync(user, dto.Password);

            if (!createResult.Succeeded)
                return IdentityFailure(createResult);

            var roleResult =
                await _userManager.AddToRoleAsync(user, role);

            if (!roleResult.Succeeded)
                return IdentityFailure(roleResult);

            await transaction.CommitAsync();

            return Success();
        });
    }

    // GET TEAM MEMBERS
    public async Task<List<TeamMemberDto>> GetTeamMembersAsync(int companyId)
    {
        if (companyId <= 0)
            return new List<TeamMemberDto>();

        var users = await _userManager.Users
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Email)
            .ToListAsync();

        var result = new List<TeamMemberDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains("CompanyAdmin"))
                continue;

            var role = roles.FirstOrDefault(x =>
                x == "ProjectManager" || x == "SiteEngineer");

            if (role == null)
                continue;

            result.Add(new TeamMemberDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                Role = role,
                CompanyId = companyId,
                IsActive = user.IsActive
            });
        }

        return result;
    }

    // UPDATE TEAM MEMBER
    public async Task<AuthResultDto> UpdateTeamMemberAsync(
        string userId,
        UpdateTeamMemberDto dto,
        int companyId)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(userId))
            return Failure("Invalid company or user.");

        if (dto == null ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Role))
        {
            return Failure("Email and role are required.");
        }

        var newRole = NormalizeTeamRole(dto.Role);

        if (newRole == null ||
            !await _roleManager.RoleExistsAsync(newRole))
        {
            return Failure("Invalid team member role.");
        }

        var email = dto.Email.Trim().ToLowerInvariant();

        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute()
            .IsValid(email))
        {
            return Failure("Invalid email address.");
        }

        var existing = await _userManager.FindByEmailAsync(email);

        if (existing != null && existing.Id != userId)
            return Failure("Email is already registered.");

        var strategy = _context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();

            await using var transaction =
                await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

            var member = await _userManager.Users
                .FirstOrDefaultAsync(x =>
                    x.Id == userId && x.CompanyId == companyId);

            if (member == null)
                return Failure("Team member not found.");

            var roles = await _userManager.GetRolesAsync(member);

            if (roles.Contains("CompanyAdmin"))
                return Failure("CompanyAdmin cannot be modified here.");

            var oldRole = roles.FirstOrDefault(x =>
                x == "ProjectManager" || x == "SiteEngineer");

            if (oldRole == null)
                return Failure("This account is not a team member.");

            if (roles.Any(x =>
                x != "ProjectManager" && x != "SiteEngineer"))
            {
                return Failure("Account has an unsupported role combination.");
            }

            if (oldRole != newRole && await _context.ProjectAssignments.AnyAsync(a =>
                a.UserId == member.Id && a.Project.CompanyId == companyId))
            {
                return Failure("Review and remove this employee's project assignments before changing their role, then reassign them under the new role.");
            }

            member.Email = email;
            member.UserName = email;

            var updateResult = await _userManager.UpdateAsync(member);

            if (!updateResult.Succeeded)
                return IdentityFailure(updateResult);

            if (oldRole != newRole)
            {
                var removeResult =
                    await _userManager.RemoveFromRolesAsync(member, roles);

                if (!removeResult.Succeeded)
                    return IdentityFailure(removeResult);

                var addResult =
                    await _userManager.AddToRoleAsync(member, newRole);

                if (!addResult.Succeeded)
                    return IdentityFailure(addResult);
            }

            // Invalidate previously issued Identity security stamps.
            await _userManager.UpdateSecurityStampAsync(member);

            await transaction.CommitAsync();

            return Success();
        });
    }

    // DEACTIVATE TEAM MEMBER
    public async Task<AuthResultDto> DeactivateTeamMemberAsync(
        string userId,
        int companyId)
    {
        if (companyId <= 0 || string.IsNullOrWhiteSpace(userId))
            return Failure("Invalid company or user.");

        var user = await _userManager.Users
            .FirstOrDefaultAsync(x =>
                x.Id == userId && x.CompanyId == companyId);

        if (user == null)
            return Failure("Team member not found.");

        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Contains("CompanyAdmin") ||
            (!roles.Contains("ProjectManager") &&
             !roles.Contains("SiteEngineer")))
        {
            return Failure("Only team members can be deactivated.");
        }

        if (!user.IsActive)
            return Success();

        user.IsActive = false;

        var result = await _userManager.UpdateAsync(user);

        if (!result.Succeeded)
            return IdentityFailure(result);

        var stampResult =
            await _userManager.UpdateSecurityStampAsync(user);

        if (!stampResult.Succeeded)
            return IdentityFailure(stampResult);

        return Success();
    }
}
