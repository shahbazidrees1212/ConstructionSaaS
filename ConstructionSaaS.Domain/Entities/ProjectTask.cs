
using System;
using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Domain.Entities;

public class ProjectTask
{
    public int Id { get; set; }

    // Tenant isolation
    public int CompanyId { get; set; }

    // Parent project
    public int ProjectId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    // Pending, InProgress, PendingReview, Completed, OnHold
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = "Pending";

    // Low, Medium, High, Critical
    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = "Medium";

    // ASP.NET Identity user ID
    public string? AssignedToUserId { get; set; }

    public DateTime? DueDateUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    // Navigation
    public Project Project { get; set; } = null!;
}
