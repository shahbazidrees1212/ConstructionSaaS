using ConstructionSaaS.Application.DTOs.Workspace;
namespace ConstructionSaaS.Web.Models;
public class AdminWorkspaceViewModel
{
    public WorkspaceReportDto Report { get; set; } = new();
    public int ProjectId { get; set; }
    public ProjectReportDto? SelectedProject => Report.Projects.FirstOrDefault(p => p.Id == ProjectId);
    public List<MilestoneDto> Milestones { get; set; } = new();
    public List<DocumentDto> Documents { get; set; } = new();
    public ActivityPage Activity { get; set; } = new();
    public string? Category { get; set; }
    public AccountDto Account { get; set; } = new();
}
