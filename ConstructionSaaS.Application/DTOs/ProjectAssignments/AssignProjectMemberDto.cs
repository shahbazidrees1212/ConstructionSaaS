
using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.ProjectAssignments;

public class AssignProjectMemberDto
{
    [Required]
    public int ProjectId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
}
