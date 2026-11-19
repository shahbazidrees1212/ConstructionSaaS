
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Repositories;

public class ProjectTaskRepository : IProjectTaskRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectTaskRepository(
        ApplicationDbContext context)
    {
        _context = context;
    }

    // =====================================
    // GET TASKS BY PROJECT
    // =====================================

    public async Task<List<ProjectTask>> GetByProjectIdAsync(
        int companyId,
        int projectId)
    {
        return await _context.ProjectTasks
            .AsNoTracking()
            .Where(t =>
                t.CompanyId == companyId &&
                t.ProjectId == projectId &&
                t.Project.CompanyId == companyId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync();
    }

    // =====================================
    // GET TASK BY ID
    // =====================================

    public async Task<ProjectTask?> GetByIdAsync(
        int companyId,
        int taskId)
    {
        return await _context.ProjectTasks
            .FirstOrDefaultAsync(t =>
                t.CompanyId == companyId &&
                t.Id == taskId &&
                t.Project.CompanyId == companyId);
    }

    // =====================================
    // CREATE TASK
    // =====================================

    public async Task AddAsync(ProjectTask task)
    {
        await _context.ProjectTasks.AddAsync(task);
        await _context.SaveChangesAsync();
    }

    // =====================================
    // UPDATE TASK
    // =====================================

    public async Task UpdateAsync(ProjectTask task)
    {
        await _context.SaveChangesAsync();
    }

    // =====================================
    // DELETE TASK
    // =====================================

    public async Task DeleteAsync(ProjectTask task)
    {
        _context.SiteWorkEntries.RemoveRange(await _context.SiteWorkEntries.Where(e => e.TaskId == task.Id).ToListAsync());
        _context.ProjectTasks.Remove(task);

        await _context.SaveChangesAsync();
    }
}
