
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace ConstructionSaaS.Infrastructure.Repositories;

public class ProjectAssignmentRepository
    : IProjectAssignmentRepository
{
    private readonly ApplicationDbContext _context;

    public ProjectAssignmentRepository(
        ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> AddAsync(
        ProjectAssignment assignment)
    {
        await _context.ProjectAssignments.AddAsync(assignment);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _context.Entry(assignment).State = EntityState.Detached;
            throw new DuplicateAssignmentException(ex);
        }
        return assignment.Id;
    }

    public async Task<List<ProjectAssignment>> GetByProjectAsync(
        int projectId,
        int companyId)
    {
        return await _context.ProjectAssignments
            .AsNoTracking()
            .Where(a =>
                a.ProjectId == projectId &&
                a.Project.CompanyId == companyId)
            .OrderByDescending(a => a.AssignedAtUtc)
            .ToListAsync();
    }

    public async Task<ProjectAssignment?> GetByIdAsync(
        int assignmentId,
        int companyId)
    {
        return await _context.ProjectAssignments
            .FirstOrDefaultAsync(a =>
                a.Id == assignmentId &&
                a.Project.CompanyId == companyId);
    }

    public async Task<bool> ExistsAsync(
        int projectId,
        string userId,
        int companyId)
    {
        return await _context.ProjectAssignments
            .AnyAsync(a =>
                a.ProjectId == projectId &&
                a.UserId == userId &&
                a.Project.CompanyId == companyId);
    }

    public async Task DeleteAsync(
        ProjectAssignment assignment)
    {
        _context.ProjectAssignments.Remove(assignment);

        await _context.SaveChangesAsync();
    }
}

