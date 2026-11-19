using ConstructionSaaS.API.Payments;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Subscriptions;

public class SubscriptionService(ApplicationDbContext db,SubscriptionCatalog catalog,ISubscriptionGateway gateway)
{
    public async Task<SubscriptionOverviewDto> Overview(int company,bool admin)
    {
        var s=await db.CompanySubscriptions.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company);
        return new() {
            BillingEnabled=catalog.Enabled,IsAdmin=admin,HasAccess=catalog.HasAccess(s,DateTime.UtcNow),IsSandbox=s?.IsSandbox??catalog.Sandbox,
            Status=s?.Status??"Not subscribed",PlanName=s?.PlanName,Provider=s?.Provider,Interval=s?.Interval,Amount=s?.Amount,Currency=s?.Currency,
            PaidThroughUtc=s?.PaidThroughUtc,LastVerifiedAtUtc=s?.LastVerifiedAtUtc,CancelAtPeriodEnd=s?.CancelAtPeriodEnd??false,
            CanSubscribe=SubscriptionCatalog.CanReplace(s,DateTime.UtcNow),CanCancel=s?.ProviderSubscriptionId!=null && s.Status is "Active" or "PastDue" or "Suspended" && !s.CancelAtPeriodEnd,
            Plans=admin?catalog.PublicPlans:[],
            Payments=admin?await db.SubscriptionPayments.AsNoTracking().Where(p=>p.CompanyId==company).OrderByDescending(p=>p.PaidAtUtc).Take(50)
                .Select(p=>new SubscriptionPaymentDto(p.Provider,p.Reference,p.Amount,p.Currency,p.PaidAtUtc,p.IsSandbox)).ToListAsync():[]
        };
    }
    public async Task<string> Checkout(int company,SubscribeInput input)
    {
        if(!catalog.Enabled)throw new PaymentUnavailableException("Subscription checkout is not enabled by the SaaS owner yet.");
        var s=await db.CompanySubscriptions.SingleOrDefaultAsync(x=>x.CompanyId==company);
        if(s!=null && !SubscriptionCatalog.CanReplace(s,DateTime.UtcNow)) {
            if(s.Status!="Pending" || s.Provider!=input.Provider || s.PlanKey!=input.PlanKey)
                throw new PaymentUnavailableException("An existing subscription or checkout must be resolved first. Refresh its status to avoid duplicate subscriptions.");
            if(s.CheckoutId!=null) {
                await Refresh(s);
                if(s.Status=="Pending" && PaymentGateway.IsCheckoutUrl(s.Provider,s.CheckoutUrl))return s.CheckoutUrl!;
                throw new PaymentUnavailableException("Checkout is no longer pending. Refresh the subscription page.");
            }
        } else {
            var plan=catalog.Plans.SingleOrDefault(p=>p.Key==input.PlanKey)??throw new PaymentUnavailableException("Choose an available subscription plan.");
            if(!catalog.Configured(input.Provider))throw new PaymentUnavailableException("This subscription provider is not configured.");
            var planId=input.Provider=="Stripe"?plan.StripePriceId:plan.PayPalPlanId;
            if(string.IsNullOrWhiteSpace(planId))throw new PaymentUnavailableException("This plan is not configured for the selected payment provider.");
            var fresh=new CompanySubscription {CompanyId=company,PlanKey=plan.Key,PlanName=plan.Name,Interval=plan.Interval,Amount=plan.Amount,Currency=plan.Currency,
                Provider=input.Provider,ProviderPlanId=planId,IsSandbox=catalog.Sandbox};
            await gateway.ValidatePlan(fresh);
            if(s==null) {s=fresh;db.CompanySubscriptions.Add(s);}
            else {var version=s.Version;db.Entry(s).CurrentValues.SetValues(fresh);s.Version=version;}
            // Persist before any external mutation: every retry uses this same attempt ID.
            await db.SaveChangesAsync();
        }
        if(s.StartedAtUtc<DateTime.UtcNow.AddHours(-5))throw new PaymentUnavailableException("This checkout needs owner reconciliation. Do not create another subscription until its provider status is checked.");
        var email=await db.Companies.Where(c=>c.Id==company).Select(c=>c.Email).SingleAsync();
        var checkout=await gateway.Create(s,email);
        if(string.IsNullOrWhiteSpace(checkout.Id)||!PaymentGateway.IsCheckoutUrl(s.Provider,checkout.Url))throw new PaymentUnavailableException("Invalid subscription checkout response.");
        s.CheckoutId=checkout.Id;s.CheckoutUrl=checkout.Url;
        if(s.Provider=="PayPal")s.ProviderSubscriptionId=checkout.Id;
        db.WorkspaceActivities.Add(new(){CompanyId=company,Category="Payments",Actor="Company subscription billing",Description=$"SaaS checkout started for {s.PlanName} through {s.Provider}."});
        await db.SaveChangesAsync();
        return checkout.Url;
    }
    public async Task Refresh(int company)
    {
        var s=await db.CompanySubscriptions.SingleOrDefaultAsync(x=>x.CompanyId==company);
        if(s!=null && (s.CheckoutId!=null||s.ProviderSubscriptionId!=null))await Refresh(s);
    }
    private async Task Refresh(CompanySubscription s)
    {
        var state=await gateway.Read(s);
        if(s.ProviderSubscriptionId!=null && s.ProviderSubscriptionId!=state.SubscriptionId)throw new PaymentUnavailableException("Subscription identity changed; contact support.");
        var previous=s.Status;
        s.ProviderSubscriptionId=state.SubscriptionId;s.CustomerId=state.CustomerId;s.Status=state.Status;s.CancelAtPeriodEnd=state.CancelAtPeriodEnd;
        if(state.PaidThroughUtc!=null && (s.PaidThroughUtc==null||state.PaidThroughUtc>s.PaidThroughUtc))s.PaidThroughUtc=state.PaidThroughUtc;
        s.LastVerifiedAtUtc=DateTime.UtcNow;
        foreach(var p in state.Payments) {
            if(string.IsNullOrWhiteSpace(p.Reference)||p.Currency!=s.Currency||p.Amount!=s.Amount)throw new PaymentUnavailableException("Subscription payment does not match the selected plan.");
            if(!await db.SubscriptionPayments.AnyAsync(x=>x.Provider==s.Provider&&x.IsSandbox==s.IsSandbox&&x.Reference==p.Reference))
                db.SubscriptionPayments.Add(new(){CompanyId=s.CompanyId,Provider=s.Provider,IsSandbox=s.IsSandbox,Reference=p.Reference,Amount=p.Amount,Currency=p.Currency,PaidAtUtc=p.PaidAtUtc});
        }
        if(previous!=s.Status)db.WorkspaceActivities.Add(new(){CompanyId=s.CompanyId,Category="Payments",Actor="SaaS subscription verification",Description=$"SaaS subscription: {previous} to {s.Status} ({s.PlanName})."});
        await db.SaveChangesAsync();
    }
    public async Task Cancel(int company)
    {
        var s=await db.CompanySubscriptions.SingleOrDefaultAsync(x=>x.CompanyId==company)??throw new PaymentUnavailableException("No subscription found.");
        await Refresh(s);
        if(s.CancelAtPeriodEnd||s.Status=="Cancelled")return;
        if(s.ProviderSubscriptionId==null||s.Status is not("Active" or "PastDue" or "Suspended"))throw new PaymentUnavailableException("There is no renewable subscription to cancel.");
        await gateway.Cancel(s);await Refresh(s);
        db.WorkspaceActivities.Add(new(){CompanyId=company,Category="Payments",Actor="Company subscription billing",Description=$"SaaS subscription renewal cancelled for {s.PlanName}."});
        await db.SaveChangesAsync();
    }
    public async Task<string> Manage(int company)
    {
        var s=await db.CompanySubscriptions.SingleOrDefaultAsync(x=>x.CompanyId==company)??throw new PaymentUnavailableException("No subscription found.");
        await Refresh(s);
        return await gateway.Manage(s);
    }
}
