using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Web.Models;

/// <summary>
/// A deliberately small projection of the Projects API used by the operational
/// workspace screens. Keeping it separate from edit models avoids posting API
/// fields back unintentionally.
/// </summary>
public class WorkspaceProjectViewModel
{
    public int Id { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? Budget { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public class WorkspaceOverviewViewModel
{
    public List<WorkspaceProjectViewModel> Projects { get; set; } = new();
    public List<TeamMemberViewModel> TeamMembers { get; set; } = new();
    public string? DataError { get; set; }

    public int ActiveProjects => Projects.Count(project =>
        string.Equals(project.Status, "Active", StringComparison.OrdinalIgnoreCase));

    public int CompletedProjects => Projects.Count(project =>
        string.Equals(project.Status, "Completed", StringComparison.OrdinalIgnoreCase));

    public int ActiveMembers => TeamMembers.Count(member => member.IsActive);
}

public class AccountSettingsViewModel
{
    [Display(Name = "Account email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Current role")]
    public string Role { get; set; } = "CompanyAdmin";

    public bool SessionActive { get; set; }
}
