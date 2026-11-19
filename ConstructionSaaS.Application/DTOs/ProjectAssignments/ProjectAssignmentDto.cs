
namespace ConstructionSaaS.Application.DTOs.ProjectAssignments;

public class ProjectAssignmentDto
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool RequiresReview { get; set; }
    public DateTime AssignedAtUtc { get; set; }
}

