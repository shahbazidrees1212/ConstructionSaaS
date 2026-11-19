using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Subscriptions;

public sealed class SubscriptionReconciler(IServiceScopeFactory scopes,ILogger<SubscriptionReconciler> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(5));
        while(await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                using var scope=scopes.CreateScope();var catalog=scope.ServiceProvider.GetRequiredService<SubscriptionCatalog>();
                if(!catalog.Enabled)continue;
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var ids=await db.CompanySubscriptions.AsNoTracking().Where(s=>s.IsSandbox==catalog.Sandbox&&(s.CheckoutId!=null||s.ProviderSubscriptionId!=null)&&s.Status!="Expired"&&(s.Status!="Cancelled"||s.PaidThroughUtc>DateTime.UtcNow))
                    .OrderBy(s=>s.LastVerifiedAtUtc).Select(s=>s.CompanyId).Take(25).ToListAsync(stoppingToken);
                foreach(var id in ids) {
                    if(stoppingToken.IsCancellationRequested)return;
                    using var item=scopes.CreateScope();
                    try{await item.ServiceProvider.GetRequiredService<SubscriptionService>().Refresh(id);}
                    catch(Exception ex) when(ex is not OperationCanceledException){logger.LogWarning("Subscription reconciliation failed for company {Company}; error type {Type}",id,ex.GetType().Name);}
                }
            } catch(Exception ex) when(ex is not OperationCanceledException) {logger.LogError("Subscription reconciliation batch failed: {Type}",ex.GetType().Name);}
        }
    }
}
