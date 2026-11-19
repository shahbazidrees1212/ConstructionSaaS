
using System.Collections.Generic;

namespace ConstructionSaaS.Web.Models
{
    public class DashboardViewModel
    {
        public ConstructionSaaS.Application.DTOs.Workspace.WorkspaceReportDto? WorkspaceReport { get; set; }
        public List<ConstructionSaaS.Application.DTOs.Workspace.ActivityDto> RecentActivity { get; set; } = new();
        public string? WorkspaceError { get; set; }
        public string AccountEmail { get; set; } = string.Empty;
        // PROJECT STATISTICS
        public int TotalProjects { get; set; }

        public int ActiveProjects { get; set; }

        public int CompletedProjects { get; set; }

        public List<DashboardProjectViewModel> Projects
        { get; set; } = new();

        // TEAM MEMBER STATISTICS
        public int TotalTeamMembers { get; set; }

        public int ActiveTeamMembers { get; set; }

        public int InactiveTeamMembers { get; set; }

        // False when Team Members API data could not be loaded.
        public bool TeamMembersLoaded { get; set; }
    }

    public class DashboardProjectViewModel
    {
        public int Id { get; set; }
        public string ProjectCode { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? City { get; set; }
        public string Country { get; set; } = string.Empty;
        public DateTime? PlannedStartDateUtc { get; set; }
        public DateTime? PlannedEndDateUtc { get; set; }
        public int? Progress { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
    }
}

