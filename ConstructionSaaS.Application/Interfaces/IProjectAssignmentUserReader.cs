
namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectAssignmentUserReader
{
    Task<ProjectAssignmentUserInfo?> GetByIdAsync(string userId);
}

public class ProjectAssignmentUserInfo
{
    public string Id { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Roles { get; set; } = new();
}

