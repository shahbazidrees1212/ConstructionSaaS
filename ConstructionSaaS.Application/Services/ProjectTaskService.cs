
using ConstructionSaaS.Application.DTOs.ProjectTasks;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Services;

public class ProjectTaskService : IProjectTaskService
{
    private readonly IProjectTaskRepository _repository;
    private readonly IProjectTaskAccessRepository _access;

    public ProjectTaskService(
        IProjectTaskRepository repository,
        IProjectTaskAccessRepository access)
    {
        _repository = repository;
        _access = access;
    }

    public async Task<List<ProjectTaskDto>> GetByProjectIdAsync(
        int companyId,
        string userId,
        int projectId)
    {
        await EnsureProjectAccessAsync(
            companyId, userId, projectId);

        var tasks = await _repository.GetByProjectIdAsync(
            companyId, projectId);

        return tasks.Select(MapToDto).ToList();
    }

    public async Task<ProjectTaskDto?> GetByIdAsync(
        int companyId,
        string userId,
        int taskId)
    {
        var task = await _repository.GetByIdAsync(
            companyId, taskId);

        if (task == null)
            return null;

        await EnsureProjectAccessAsync(
            companyId, userId, task.ProjectId);

        return MapToDto(task);
    }

    public async Task<ProjectTaskDto> CreateAsync(
        int companyId,
        string userId,
        CreateProjectTaskDto dto)
    {
        await EnsureProjectAccessAsync(
            companyId, userId, dto.ProjectId);

        await EnsureValidAssigneeAsync(
            companyId, dto.ProjectId, dto.AssignedToUserId);

        if (dto.DueDateUtc.HasValue && (dto.DueDateUtc.Value.Year < 2000 || dto.DueDateUtc.Value.Year > 2100)) throw new ArgumentException("Choose a due date between 2000 and 2100.");
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException(
                "Task title is required.");

        var task = new ProjectTask
        {
            CompanyId = companyId,
            ProjectId = dto.ProjectId,
            Title = dto.Title.Trim(),
            Description = dto.Description,
            Priority = dto.Priority,
            Status = "Pending",
            AssignedToUserId = string.IsNullOrWhiteSpace(dto.AssignedToUserId) ? null : dto.AssignedToUserId,
            DueDateUtc = dto.DueDateUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _repository.AddAsync(task);

        return MapToDto(task);
    }

    public async Task<bool> UpdateAsync(
        int companyId,
        string userId,
        int taskId,
        UpdateProjectTaskDto dto)
    {
        var task = await _repository.GetByIdAsync(
            companyId, taskId);

        if (task == null)
            return false;

        await EnsureProjectAccessAsync(
            companyId, userId, task.ProjectId);

        await EnsureValidAssigneeAsync(
            companyId, task.ProjectId, dto.AssignedToUserId);

        if (dto.DueDateUtc.HasValue && (dto.DueDateUtc.Value.Year < 2000 || dto.DueDateUtc.Value.Year > 2100)) throw new ArgumentException("Choose a due date between 2000 and 2100.");
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException(
                "Task title is required.");

        if (task.Status != dto.ExpectedStatus) throw new InvalidOperationException("Task status changed. Refresh before editing.");
        if (task.Status is "PendingReview" or "Completed")
            throw new ArgumentException("Reopen this task with a reason before editing its details or assignment.");
        task.Title = dto.Title.Trim();
        task.Description = dto.Description;
        if (dto.Status != task.Status) throw new ArgumentException("Use the task status/review action to change status.");
        task.Priority = dto.Priority;
        task.AssignedToUserId = string.IsNullOrWhiteSpace(dto.AssignedToUserId) ? null : dto.AssignedToUserId;
        task.DueDateUtc = dto.DueDateUtc;
        task.UpdatedAtUtc = DateTime.UtcNow;

        await _repository.UpdateAsync(task);

        return true;
    }

    public async Task<bool> DeleteAsync(
        int companyId,
        string userId,
        int taskId)
    {
        var task = await _repository.GetByIdAsync(
            companyId, taskId);

        if (task == null)
            return false;

        await EnsureProjectAccessAsync(
            companyId, userId, task.ProjectId);

        await _repository.DeleteAsync(task);

        return true;
    }

    private async Task EnsureProjectAccessAsync(
        int companyId,
        string userId,
        int projectId)
    {
        var allowed = await _access.CanManageProjectAsync(
            companyId, userId, projectId);

        if (!allowed)
            throw new UnauthorizedAccessException(
                "You are not assigned to this project.");
    }

    private async Task EnsureValidAssigneeAsync(
        int companyId,
        int projectId,
        string? assignedToUserId)
    {
        if (string.IsNullOrWhiteSpace(assignedToUserId))
            return;

        var valid = await _access.IsValidSiteEngineerAsync(
            companyId, assignedToUserId);

        if (!valid || !await _access.CanManageProjectAsync(companyId, assignedToUserId, projectId))
            throw new ArgumentException(
                "Choose an active SiteEngineer assigned to this project.");
    }

    private static ProjectTaskDto MapToDto(ProjectTask task)
    {
        return new ProjectTaskDto
        {
            Id = task.Id,
            ProjectId = task.ProjectId,
            Title = task.Title,
            Description = task.Description,
            Status = task.Status,
            Priority = task.Priority,
            AssignedToUserId = task.AssignedToUserId,
            DueDateUtc = task.DueDateUtc,
            CreatedAtUtc = task.CreatedAtUtc,
            UpdatedAtUtc = task.UpdatedAtUtc
        };
    }
}
