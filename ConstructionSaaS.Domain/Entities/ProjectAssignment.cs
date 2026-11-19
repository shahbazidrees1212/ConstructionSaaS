
namespace ConstructionSaaS.Domain.Entities;

public class ProjectAssignment
{
    public int Id { get; set; }

    // Project being assigned
    public int ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    // ASP.NET Core Identity user ID
    public string UserId { get; set; } = string.Empty;

    // When the assignment was created
    public DateTime AssignedAtUtc { get; set; }
        = DateTime.UtcNow;
}
