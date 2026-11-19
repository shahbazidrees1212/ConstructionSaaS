
using System;
using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.ProjectTasks;

public class UpdateProjectTaskDto
{
    [Required] public string ExpectedStatus { get; set; } = string.Empty;
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    [RegularExpression("^(Pending|InProgress|PendingReview|Completed|OnHold)$")]
    public string Status { get; set; } = "Pending";

    [Required]
    [RegularExpression("Low|Medium|High|Critical")]
    public string Priority { get; set; } = "Medium";

    public string? AssignedToUserId { get; set; }

    public DateTime? DueDateUtc { get; set; }
}
