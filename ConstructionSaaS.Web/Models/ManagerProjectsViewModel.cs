
using System.Collections.Generic;
using ConstructionSaaS.Application.DTOs.Projects;

namespace ConstructionSaaS.Web.Models;

public class ManagerProjectsViewModel
{
    public string AccountEmail { get; set; } = string.Empty;

    public List<ProjectDto> Projects { get; set; } = new();

    public int TotalProjects =>
        Projects.Count;

    public int ActiveProjects =>
        Projects.Count(p => p.Status == "Active");

    public int CompletedProjects =>
        Projects.Count(p => p.Status == "Completed");

    public string? ErrorMessage { get; set; }

    public bool ProjectsLoaded { get; set; }
}
