
using ConstructionSaaS.Application.DTOs.ProjectTasks;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectTaskService
{
    // Get all tasks for an assigned project
    Task<List<ProjectTaskDto>> GetByProjectIdAsync(
        int companyId,
        string userId,
        int projectId);

    // Get a single task
    Task<ProjectTaskDto?> GetByIdAsync(
        int companyId,
        string userId,
        int taskId);

    // Create a new task
    Task<ProjectTaskDto> CreateAsync(
        int companyId,
        string userId,
        CreateProjectTaskDto dto);

    // Update an existing task
    Task<bool> UpdateAsync(
        int companyId,
        string userId,
        int taskId,
        UpdateProjectTaskDto dto);

    // Delete a task
    Task<bool> DeleteAsync(
        int companyId,
        string userId,
        int taskId);
}
