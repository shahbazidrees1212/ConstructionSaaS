using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectRepository
{
    Task<int> AddAsync(Project project);
    Task<List<Project>> GetAllAsync(int companyId);
    Task<Project?> GetByIdAsync(int id, int companyId);
    Task UpdateAsync(Project project);
    Task DeleteAsync(Project project);
}