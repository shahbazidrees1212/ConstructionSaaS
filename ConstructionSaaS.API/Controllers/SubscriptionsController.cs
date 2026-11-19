using System.Security.Claims;
using System.Text.Json;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.API.Subscriptions;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;

[ApiController,Route("api/subscriptions"),Authorize,TypeFilter(typeof(PaymentExceptionFilter))]
public class SubscriptionsController(SubscriptionService service,SubscriptionCatalog catalog,ISubscriptionGateway gateway,ApplicationDbContext db):ControllerBase
{
    private int Company=>int.TryParse(User.FindFirstValue("company_id"),out var id)?id:0;
    [HttpGet] public async Task<IActionResult> Overview()=>Company<=0?Forbid():Ok(await service.Overview(Company,User.IsInRole("CompanyAdmin")));
    [HttpPost("checkout"),Authorize(Roles="CompanyAdmin")]
    public async Task<IActionResult> Checkout(SubscribeInput input)=>Company<=0?Forbid():Ok(new PaymentLinkDto(await service.Checkout(Company,input)));
    [HttpPost("refresh"),Authorize(Roles="CompanyAdmin")]
    public async Task<IActionResult> Refresh(){if(Company<=0)return Forbid();await service.Refresh(Company);return Ok(await service.Overview(Company,true));}
    [HttpPost("cancel"),Authorize(Roles="CompanyAdmin")]
    public async Task<IActionResult> Cancel(){if(Company<=0)return Forbid();await service.Cancel(Company);return Ok(await service.Overview(Company,true));}
    [HttpGet("companies"),Authorize(Roles="PlatformAdmin")]
    public async Task<IActionResult> Companies()=>Ok(await db.Companies.AsNoTracking().OrderBy(c=>c.Name).Select(c=>new {
        c.Id,c.Name,c.Email,Subscription=db.CompanySubscriptions.Where(s=>s.CompanyId==c.Id).Select(s=>new{s.PlanName,s.Provider,s.Status,s.IsSandbox,s.PaidThroughUtc,s.CancelAtPeriodEnd,s.LastVerifiedAtUtc}).FirstOrDefault()
    }).ToListAsync());
    [HttpPost("portal"),Authorize(Roles="CompanyAdmin")]
    public async Task<IActionResult> Portal()=>Company<=0?Forbid():Ok(new PaymentLinkDto(await service.Manage(Company)));
    [HttpPost("companies/{id:int}/refresh"),Authorize(Roles="PlatformAdmin")]
    public async Task<IActionResult> OwnerRefresh(int id){await service.Refresh(id);return NoContent();}
    [HttpPost("webhooks/{provider}"),AllowAnonymous,RequestSizeLimit(262144)]
    public async Task<IActionResult> Webhook(string provider)
    {
        provider=provider.ToLowerInvariant() switch{"stripe"=>"Stripe","paypal"=>"PayPal",_=>""};
        if(provider=="")return NotFound();
        if(!catalog.Enabled||!catalog.Configured(provider))return StatusCode(503);
        using var reader=new StreamReader(Request.Body);var body=await reader.ReadToEndAsync();
        if(!await gateway.Verify(provider,body,Request.Headers))return BadRequest();
        using var document=JsonDocument.Parse(body);var root=document.RootElement;
        string? subscriptionId=null;Guid? attempt=null;
        static string? Text(JsonElement e,string field)=>e.TryGetProperty(field,out var x)&&x.ValueKind==JsonValueKind.String?x.GetString():null;
        if(provider=="Stripe") {
            var type=Text(root,"type")??"";
            if(!(type.StartsWith("checkout.session.")||type.StartsWith("customer.subscription.")||type.StartsWith("invoice.")))return Ok();
            var resource=root.GetProperty("data").GetProperty("object");
            if(type.StartsWith("customer.subscription."))subscriptionId=Text(resource,"id");
            else subscriptionId=Text(resource,"subscription");
            if(subscriptionId==null && resource.TryGetProperty("parent",out var parent)&&parent.ValueKind==JsonValueKind.Object&&parent.TryGetProperty("subscription_details",out var details)&&details.ValueKind==JsonValueKind.Object)subscriptionId=Text(details,"subscription");
            if(resource.TryGetProperty("metadata",out var metadata)&&Guid.TryParse(Text(metadata,"attempt_id"),out var parsed))attempt=parsed;
            if(attempt==null&&Guid.TryParse(Text(resource,"client_reference_id"),out parsed))attempt=parsed;
        } else {
            var type=Text(root,"event_type")??"";
            if(!(type.StartsWith("BILLING.SUBSCRIPTION.")||type.StartsWith("PAYMENT.SALE.")))return Ok();
            var resource=root.GetProperty("resource");
            subscriptionId=type.StartsWith("BILLING.SUBSCRIPTION.")?Text(resource,"id"):Text(resource,"billing_agreement_id");
            if(Guid.TryParse(Text(resource,"custom_id"),out var parsed))attempt=parsed;
        }
        var s=await db.CompanySubscriptions.AsNoTracking().SingleOrDefaultAsync(s=>s.Provider==provider&&s.IsSandbox==catalog.Sandbox&&
            ((subscriptionId!=null&&s.ProviderSubscriptionId==subscriptionId)||(attempt!=null&&s.AttemptId==attempt)));
        if(s==null)return Ok();
        // Never grant access from the event payload: reload authoritative state. Rowversion rejects concurrent stale writes.
        if(s.CheckoutId==null && s.ProviderSubscriptionId==null)return StatusCode(503);
        await service.Refresh(s.CompanyId);return Ok();
    }
}
