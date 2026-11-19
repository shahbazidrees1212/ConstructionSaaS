using System.ComponentModel.DataAnnotations;
using ConstructionSaaS.Application.DTOs.Workspace;
namespace ConstructionSaaS.Application.DTOs.ProjectTasks;

public class TaskWorkspaceDto
{
    public bool IsManager { get; set; }
    public List<WorkEntryDto> WorkEntries { get; set; } = new();
    public List<TaskProjectDto> Projects { get; set; } = new();
    public List<TaskItemDto> Tasks { get; set; } = new();
    public List<TaskEngineerDto> Engineers { get; set; } = new();
    public List<TaskNoticeDto> Notifications { get; set; } = new();
    public List<ActivityDto> History { get; set; } = new();
    public int OverdueMilestones { get; set; }
}
public class TaskProjectDto { public int Id { get; set; } public string Name { get; set; } = ""; }
public class TaskEngineerDto { public int ProjectId { get; set; } public string UserId { get; set; } = ""; public string Email { get; set; } = ""; }
public class TaskItemDto : ProjectTaskDto
{
    public string ProjectName { get; set; } = "";
    public string? AssigneeEmail { get; set; }
    public bool AssigneeEligible { get; set; }
    public bool IsOverdue => Status != "Completed" && DueDateUtc.HasValue && DueDateUtc.Value.Date < DateTime.UtcNow.Date;
}
public class TaskNoticeDto { public long Id { get; set; } public int ProjectId { get; set; } public string Message { get; set; } = ""; public DateTime CreatedAtUtc { get; set; } public bool IsRead { get; set; } }
public class TaskStatusInput
{
    [StringLength(3000)] public string? Note { get; set; }
    [Required, RegularExpression("^(Pending|InProgress|PendingReview|Completed|OnHold)$")] public string Status { get; set; } = "Pending";
    [Required, RegularExpression("^(Pending|InProgress|PendingReview|Completed|OnHold)$")] public string ExpectedStatus { get; set; } = "Pending";
}
