using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;

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
}