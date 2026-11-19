
using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Web.Models;

public class ProjectAssignmentsViewModel
{
    [Range(1, int.MaxValue,
        ErrorMessage = "Please select a project.")]
    public int ProjectId { get; set; }

    [Required(ErrorMessage = "Please select an employee.")]
    public string UserId { get; set; } = string.Empty;

    public List<ProjectOptionViewModel> Projects { get; set; } = new();

    public List<TeamMemberViewModel> Members { get; set; } = new();

    public List<ProjectAssignmentItemViewModel> Assignments
    { get; set; } = new();
}

public class ProjectOptionViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ProjectCode { get; set; } = string.Empty;
}

public class ProjectAssignmentItemViewModel
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

