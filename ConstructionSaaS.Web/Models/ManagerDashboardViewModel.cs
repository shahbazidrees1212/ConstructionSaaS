
using System.Collections.Generic;
using ConstructionSaaS.Application.DTOs.Projects;

namespace ConstructionSaaS.Web.Models;

public class ManagerDashboardViewModel
{
    public ConstructionSaaS.Application.DTOs.Workspace.WorkspaceReportDto? Report { get; set; }
    public int CompanyId { get; set; }

    public string AccountEmail { get; set; } = string.Empty;

    public int TotalProjects { get; set; }

    public int ActiveProjects { get; set; }

    public int CompletedProjects { get; set; }

    public int PlanningProjects { get; set; }

    public List<ProjectDto> Projects { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public bool ProjectsLoaded { get; set; }
}
