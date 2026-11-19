namespace ConstructionSaaS.Web.Controllers;

// Reuse the validated forms and API error handling, with manager-specific access and views.
public class ManagerWorkspaceController(IHttpClientFactory factory, IConfiguration configuration)
    : WorkspaceController(factory, configuration)
{
    protected override string WorkspaceRole => "ProjectManager";
}
