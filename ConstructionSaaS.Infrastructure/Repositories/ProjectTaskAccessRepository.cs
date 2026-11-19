
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Repositories;

public class ProjectTaskAccessRepository
    : IProjectTaskAccessRepository
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ProjectTaskAccessRepository(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    // ==========================================
    // CHECK PROJECT MANAGER ASSIGNMENT
    // ==========================================

    public async Task<bool> CanManageProjectAsync(
        int companyId,
        string userId,
        int projectId)
    {
        if (companyId <= 0 ||
            projectId <= 0 ||
            string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return await _context.Projects
            .AsNoTracking()
            .AnyAsync(p =>
                p.Id == projectId &&
                p.CompanyId == companyId &&
                p.Assignments.Any(a =>
                    a.UserId == userId));
    }

    // ==========================================
    // VALIDATE SITE ENGINEER
    // ==========================================

    public async Task<bool> IsValidSiteEngineerAsync(
        int companyId,
        string userId)
    {
        if (companyId <= 0 ||
            string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null || !user.IsActive)
            return false;

        if (user.CompanyId != companyId)
            return false;

        var isSiteEngineer =
            await _userManager.IsInRoleAsync(
                user, "SiteEngineer");

        return isSiteEngineer;
    }
}
