namespace ConstructionSaaS.Web.Controllers;
public class EngineerWorkspaceController(IHttpClientFactory factory, IConfiguration configuration) : WorkspaceController(factory, configuration)
{
    protected override string WorkspaceRole => "SiteEngineer";
}
