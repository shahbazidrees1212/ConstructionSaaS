using System.Security.Claims;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Subscriptions;

public class SubscriptionAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context,SubscriptionCatalog catalog,ApplicationDbContext db)
    {
        var path=context.Request.Path;
        if(!catalog.Enabled||context.User.Identity?.IsAuthenticated!=true||context.User.IsInRole("PlatformAdmin")||
            context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>()!=null||path.StartsWithSegments("/api/subscriptions")||
            path.StartsWithSegments("/health")||path.StartsWithSegments("/api/account")) {await next(context);return;}
        if(!int.TryParse(context.User.FindFirstValue("company_id"),out var company)){context.Response.StatusCode=403;return;}
        var s=await db.CompanySubscriptions.AsNoTracking().SingleOrDefaultAsync(s=>s.CompanyId==company);
        if(!catalog.HasAccess(s,DateTime.UtcNow)) {
            context.Response.StatusCode=402;
            await context.Response.WriteAsJsonAsync(new{code="subscription_required",message="Your company needs an active subscription. Ask the CompanyAdmin to open Subscription & billing."});return;
        }
        await next(context);
    }
}
