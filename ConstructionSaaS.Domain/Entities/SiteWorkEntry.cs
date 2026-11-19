namespace ConstructionSaaS.Domain.Entities;

// Append-only work updates; taskless entries are daily site reports.
public class SiteWorkEntry
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int? TaskId { get; set; }
    public ProjectTask? Task { get; set; }
    public string AuthorUserId { get; set; } = "";
    public string AuthorEmail { get; set; } = "";
    public string Kind { get; set; } = "Note";
    public string Summary { get; set; } = "";
    public string? Blockers { get; set; }
    public DateTime? ReportDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public byte[]? Content { get; set; }
}
