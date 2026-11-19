
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectRepository
{
    // Existing methods
    Task<int> AddAsync(Project project);

    Task<List<Project>> GetAllAsync(int companyId);

    Task<Project?> GetByIdAsync(int id, int companyId);

    Task UpdateAsync(Project project);

    Task DeleteAsync(Project project);

    // NEW: Only projects assigned to the logged-in user
    Task<List<Project>> GetAssignedProjectsAsync(
        int companyId,
        string userId);

    // NEW: Get one assigned project securely
    Task<Project?> GetAssignedProjectByIdAsync(
        int projectId,
        int companyId,
        string userId);
}
