using System.Security.Claims;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace ConstructionSaaS.API.Controllers;
[ApiController, Route("api/notifications"), Authorize(Roles="CompanyAdmin,ProjectManager,SiteEngineer")]
public class NotificationsController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet("stream")]
    public async Task Stream([FromServices] NotificationSignal signals, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue("company_id"), out var company) || company <= 0 ||
            string.IsNullOrEmpty(User.FindFirstValue(ClaimTypes.NameIdentifier)))
        { Response.StatusCode = 403; return; }
        using var subscription = signals.Subscribe(company, User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        // Rotate connections to re-run JWT/security-stamp validation.
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        if (long.TryParse(User.FindFirstValue("exp"), out var expiry))
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(expiry) - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) { Response.StatusCode = 401; return; }
            if (remaining < TimeSpan.FromSeconds(60)) lifetime.CancelAfter(remaining);
        }
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var stamp = User.FindFirstValue("security_stamp");
                var account = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.CompanyId == company &&
                    u.IsActive && u.SecurityStamp == stamp, lifetime.Token);
                var currentRoles = await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id
                    where ur.UserId == userId select role.Name).ToListAsync(lifetime.Token);
                var claimedRoles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
                var companyActive = await db.Companies.AsNoTracking()
                    .AnyAsync(c => c.Id == company && c.IsActive, lifetime.Token);
                if (!account || !companyActive || currentRoles.Count != claimedRoles.Count || claimedRoles.Except(currentRoles).Any())
                {
                    await Response.WriteAsync("event: unauthorized\ndata: {}\n\n", lifetime.Token);
                    await Response.Body.FlushAsync(lifetime.Token);
                    return;
                }
                var snapshot = (OkObjectResult)await Index(lifetime.Token);
                var json = JsonSerializer.Serialize(snapshot.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                await Response.WriteAsync("data: " + json + "\n\n", lifetime.Token);
                await Response.Body.FlushAsync(lifetime.Token);
                // Timeout also reconciles other API processes and changed project access.
                await subscription.WaitAsync(lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue("company_id"), out var company) || company <= 0) return Forbid();
        var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(user)) return Forbid();
        var admin = User.IsInRole("CompanyAdmin");
        var unread = await db.WorkspaceNotifications.Where(n => n.CompanyId == company &&
            n.RecipientUserId == user && n.ReadAtUtc == null && db.Projects.Any(p =>
                p.Id == n.ProjectId && p.CompanyId == company && (admin || p.Assignments.Any(a => a.UserId == user))))
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var notice in unread) notice.ReadAtUtc = now;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if(!int.TryParse(User.FindFirstValue("company_id"),out var company) || company<=0) return Forbid();
        var user=User.FindFirstValue(ClaimTypes.NameIdentifier);
        if(string.IsNullOrEmpty(user)) return Forbid();
        var admin=User.IsInRole("CompanyAdmin");
        var projects=db.Projects.Where(p=>p.CompanyId==company && (admin || p.Assignments.Any(a=>a.UserId==user))).Select(p=>p.Id);
        var query=db.WorkspaceNotifications.AsNoTracking().Where(n=>n.CompanyId==company && n.RecipientUserId==user && projects.Contains(n.ProjectId));
        var unread=await query.CountAsync(n=>n.ReadAtUtc==null,ct);
        var items=await query.OrderBy(n=>n.ReadAtUtc!=null).ThenByDescending(n=>n.CreatedAtUtc).ThenByDescending(n=>n.Id).Take(20).Select(n=>new {n.Id,n.Message,n.ProjectId,n.CreatedAtUtc,isRead=n.ReadAtUtc!=null}).ToListAsync(ct);
        if (!Response.HasStarted) Response.Headers.CacheControl="no-store";
        return Ok(new {unread,items = items.Select(n => new { n.Id, n.Message, n.ProjectId, n.CreatedAtUtc, n.isRead, url = (admin ? "/Workspace/Activity?projectId=" : "/TaskBoard/Index?projectId=") + n.ProjectId })});
    }
    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> Read(long id,CancellationToken ct)
    {
        if(!int.TryParse(User.FindFirstValue("company_id"),out var company)) return Forbid();
        var user=User.FindFirstValue(ClaimTypes.NameIdentifier); var admin=User.IsInRole("CompanyAdmin");
        var notice=await db.WorkspaceNotifications.FirstOrDefaultAsync(n=>n.Id==id && n.CompanyId==company && n.RecipientUserId==user && db.Projects.Any(p=>p.Id==n.ProjectId && p.CompanyId==company && (admin || p.Assignments.Any(a=>a.UserId==user))),ct);
        if(notice==null)return NotFound();
        notice.ReadAtUtc ??= DateTime.UtcNow; await db.SaveChangesAsync(ct); return NoContent();
    }
}
