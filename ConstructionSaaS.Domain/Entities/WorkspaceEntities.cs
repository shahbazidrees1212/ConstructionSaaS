namespace ConstructionSaaS.Domain.Entities;

public class ProjectMilestone
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime DueDateUtc { get; set; }
    public int Progress { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ProjectDocument
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public long Size { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string? UploadedByUserId { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkspaceActivity
{
    public int? ProjectId { get; set; }
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Actor { get; set; } = "System";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public class WorkspaceNotification
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public int ProjectId { get; set; }
    public string RecipientUserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}
