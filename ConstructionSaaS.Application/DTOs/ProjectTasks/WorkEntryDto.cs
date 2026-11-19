using System.ComponentModel.DataAnnotations;
namespace ConstructionSaaS.Application.DTOs.ProjectTasks;
public class WorkEntryInput
{
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    public int? TaskId { get; set; }
    [Required, StringLength(3000)] public string Summary { get; set; } = "";
    [StringLength(2000)] public string? Blockers { get; set; }
    public DateTime? ReportDate { get; set; }
}
public class WorkEntryDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? TaskId { get; set; }
    public string AuthorEmail { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
    public string? Blockers { get; set; }
    public DateTime? ReportDate { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? FileName { get; set; }
}
