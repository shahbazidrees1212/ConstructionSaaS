
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Repositories;

public class ProjectRepository : IProjectRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> AddAsync(Project project)
    {
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        return project.Id;
    }

    // CompanyAdmin: all projects within their company
    public async Task<List<Project>> GetAllAsync(int companyId)
    {
        return await _context.Projects
            .Where(p => p.CompanyId == companyId)
            .AsNoTracking()
            .ToListAsync();
    }

    // CompanyAdmin: one project within their company
    public async Task<Project?> GetByIdAsync(int id, int companyId)
    {
        return await _context.Projects
            .FirstOrDefaultAsync(p =>
                p.Id == id && p.CompanyId == companyId);
    }

    // NEW: Projects assigned to the logged-in user
    public async Task<List<Project>> GetAssignedProjectsAsync(
        int companyId,
        string userId)
    {
        return await _context.Projects
            .AsNoTracking()
            .Where(p =>
                p.CompanyId == companyId &&
                p.Assignments.Any(a => a.UserId == userId))
            .OrderBy(p => p.Id)
            .ToListAsync();
    }

    // NEW: Access only an assigned project
    public async Task<Project?> GetAssignedProjectByIdAsync(
        int projectId,
        int companyId,
        string userId)
    {
        return await _context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.Id == projectId &&
                p.CompanyId == companyId &&
                p.Assignments.Any(a => a.UserId == userId));
    }

    public async Task UpdateAsync(Project project)
    {
        _context.Projects.Update(project);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Project project)
    {
        var strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction =
                await _context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);

            if (await _context.ProjectInvoices.AnyAsync(i => i.ProjectId == project.Id))
                throw new ProjectHasInvoicesException();

            await _context.SiteWorkEntries.Where(e => e.ProjectId == project.Id && e.Project.CompanyId == project.CompanyId).ExecuteDeleteAsync();
            await _context.ProjectTasks.Where(t => t.ProjectId == project.Id && t.CompanyId == project.CompanyId).ExecuteDeleteAsync();
            await _context.ProjectAssignments
                .Where(a =>
                    a.ProjectId == project.Id &&
                    a.Project.CompanyId == project.CompanyId)
                .ExecuteDeleteAsync();

            await _context.Projects
                .Where(p =>
                    p.Id == project.Id &&
                    p.CompanyId == project.CompanyId)
                .ExecuteDeleteAsync();

            _context.WorkspaceActivities.Add(new WorkspaceActivity
            {
                CompanyId = project.CompanyId,
                Category = "Projects",
                Description =
                    "Deleted project and its assignments, milestones and documents: "
                    + project.Name,
                Actor = _context.AuditActor
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _context.Entry(project).State = EntityState.Detached;
        });
    }
}
