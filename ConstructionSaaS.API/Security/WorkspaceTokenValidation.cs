
using System.Security.Claims;
using ConstructionSaaS.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Security;

public static class WorkspaceTokenValidation
{
    public static async Task ValidateAsync(
        TokenValidatedContext context)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<
                ILoggerFactory>()
            .CreateLogger("WorkspaceTokenValidation");

        var users = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        var principal = context.Principal;

        if (principal == null)
        {
            logger.LogWarning("JWT rejected: Missing identity.");
            context.Fail("Missing identity.");
            return;
        }

        var id = principal.FindFirstValue(
            ClaimTypes.NameIdentifier);

        var user = string.IsNullOrWhiteSpace(id)
            ? null
            : await users.FindByIdAsync(id);

        if (user == null)
        {
            logger.LogWarning(
                "JWT rejected: User not found.");

            context.Fail("Account is unavailable.");
            return;
        }

        if (!user.IsActive)
        {
            logger.LogWarning(
                "JWT rejected: Account inactive.");

            context.Fail("Account is unavailable.");
            return;
        }

        var tokenCompanyId =
            principal.FindFirstValue("company_id");

        if (user.CompanyId.HasValue && !await context.HttpContext.RequestServices
            .GetRequiredService<ApplicationDbContext>().Companies.AsNoTracking()
            .AnyAsync(c => c.Id == user.CompanyId.Value && c.IsActive, context.HttpContext.RequestAborted))
        {
            context.Fail("Company is unavailable.");
            return;
        }

        if (tokenCompanyId != user.CompanyId?.ToString())
        {
            logger.LogWarning(
                "JWT rejected: Company mismatch.");

            context.Fail("Company has changed.");
            return;
        }

        var tokenSecurityStamp =
            principal.FindFirstValue("security_stamp");

        if (string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            tokenSecurityStamp != user.SecurityStamp)
        {
            logger.LogWarning(
                "JWT rejected: Security stamp mismatch.");

            context.Fail(
                "Account security has changed.");
            return;
        }

        var roles = await users.GetRolesAsync(user);

        var claimedRoles = principal
            .FindAll(ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

        if (claimedRoles.Count == 0 ||
            claimedRoles.Count != roles.Count ||
            claimedRoles.Except(
                roles,
                StringComparer.OrdinalIgnoreCase).Any())
        {
            logger.LogWarning(
                "JWT rejected: Role mismatch.");

            context.Fail("Account roles have changed.");
            return;
        }

        logger.LogInformation(
            "JWT workspace validation successful.");
    }
}
