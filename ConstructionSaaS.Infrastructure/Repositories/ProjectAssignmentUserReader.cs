
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace ConstructionSaaS.Infrastructure.Repositories;

public class ProjectAssignmentUserReader
    : IProjectAssignmentUserReader
{
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectAssignmentUserReader(
        UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ProjectAssignmentUserInfo?> GetByIdAsync(
        string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
            return null;

        var roles = await _userManager.GetRolesAsync(user);

        return new ProjectAssignmentUserInfo
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            CompanyId = user.CompanyId,
            IsActive = user.IsActive,
            Roles = roles.ToList()
        };
    }
}

