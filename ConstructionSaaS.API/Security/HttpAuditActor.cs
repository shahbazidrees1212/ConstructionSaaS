using System.Security.Claims;
using ConstructionSaaS.Application.Interfaces;
namespace ConstructionSaaS.API.Security;
public class HttpAuditActor(IHttpContextAccessor accessor) : IAuditActor
{
    public string Name => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email) ?? "System";
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
}
