using System.ComponentModel.DataAnnotations;
namespace ConstructionSaaS.Application.DTOs.Workspace;

public class MilestoneInput
{
    [Required, StringLength(160, MinimumLength = 1)] public string Title { get; set; } = string.Empty;
    [StringLength(2000)] public string? Notes { get; set; }
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.Date;
    [Range(0, 100)] public int Progress { get; set; }
}
public class MilestoneDto : MilestoneInput
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool IsOverdue => Progress < 100 && DueDateUtc.Date < DateTime.UtcNow.Date;
}
public class DocumentDto
{
    public bool CanDelete { get; set; }
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
}
public class ActivityDto
{
    public long Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
}
public class ActivityPage
{
    public List<ActivityDto> Items { get; set; } = new();
    public int Page { get; set; }
    public bool HasMore { get; set; }
}
public class ProjectReportDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal? Budget { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public int Milestones { get; set; }
    public int CompletedMilestones { get; set; }
    public int OverdueMilestones { get; set; }
    public int? Progress { get; set; }
    public int Assignments { get; set; }
    public int Documents { get; set; }
    public int Tasks { get; set; }
    public int CompletedTasks { get; set; }
    public int OverdueTasks { get; set; }
    public bool IsOverdue => Status != "Completed" && Status != "Cancelled" && End.HasValue && End.Value.Date < DateTime.UtcNow.Date;
}
public class WorkspaceReportDto
{
    public List<ProjectReportDto> Projects { get; set; } = new();
    public int ActiveMembers { get; set; }
    public int InactiveMembers { get; set; }
}
public class ProfileInput
{
    [Required, StringLength(100)] public string FullName { get; set; } = "";
    [StringLength(100)] public string? JobTitle { get; set; }
    [StringLength(40), Phone] public string? Phone { get; set; }
    [StringLength(100)] public string? Location { get; set; }
    [StringLength(500)] public string? Bio { get; set; }
}
public class AccountDto : ProfileInput
{
    public bool HasPhoto { get; set; }
    public string CompanyName { get; set; } = "";
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
public class ChangeEmailInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
    [Required] public string CurrentPassword { get; set; } = string.Empty;
}
public class ChangePasswordInput
{
    [Required] public string CurrentPassword { get; set; } = string.Empty;
    [Required, MinLength(8), StringLength(128)] public string NewPassword { get; set; } = string.Empty;
    [Required, Compare(nameof(NewPassword))] public string ConfirmPassword { get; set; } = string.Empty;
}
