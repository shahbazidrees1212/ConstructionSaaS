
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectTaskRepository
{
    // Get all tasks of a specific company and project
    Task<List<ProjectTask>> GetByProjectIdAsync(
        int companyId,
        int projectId);

    // Get a single task
    Task<ProjectTask?> GetByIdAsync(
        int companyId,
        int taskId);

    // Create a new task
    Task AddAsync(ProjectTask task);

    // Update an existing task
    Task UpdateAsync(ProjectTask task);

    // Delete a task
    Task DeleteAsync(ProjectTask task);
}
