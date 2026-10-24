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

    public async Task<List<Project>> GetAllAsync(int companyId)
    {
        return await _context.Projects
            .Where(p => p.CompanyId == companyId)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<Project?> GetByIdAsync(int id, int companyId)
    {
        return await _context.Projects
            .FirstOrDefaultAsync(p =>
                p.Id == id && p.CompanyId == companyId);
    }

    public async Task UpdateAsync(Project project)
    {
        _context.Projects.Update(project);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Project project)
    {
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();
    }
}