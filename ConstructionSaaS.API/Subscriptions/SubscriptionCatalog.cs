using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.API.Payments;

namespace ConstructionSaaS.API.Subscriptions;

public record SubscriptionPlan(string Key, string Name, string Description, string Interval, decimal Amount, string Currency, string? StripePriceId, string? PayPalPlanId);
public class SubscriptionCatalog(IConfiguration config, IHostEnvironment environment)
{
    public bool Enabled => config.GetValue<bool>("Subscriptions:Enabled");
    public bool Sandbox => config["Subscriptions:Mode"] != "Live";
    public bool CanGrantAccess => Enabled && (!Sandbox || environment.IsDevelopment() || environment.IsEnvironment("Testing"));
    public string? Setting(string provider, string key) => config[$"Subscriptions:{provider}:{key}"];
    public bool Configured(string provider) => config["Subscriptions:Mode"] is "Sandbox" or "Live" && (provider switch {
        "Stripe" => Setting(provider,"SecretKey")?.StartsWith(Sandbox ? "sk_test_" : "sk_live_", StringComparison.Ordinal)==true && !string.IsNullOrWhiteSpace(Setting(provider,"WebhookSecret")),
        "PayPal" => new[]{"ClientId","ClientSecret","WebhookId"}.All(k=>!string.IsNullOrWhiteSpace(Setting(provider,k)) && !Setting(provider,k)!.StartsWith("YOUR_",StringComparison.Ordinal)),
        _ => false
    });
    public List<SubscriptionPlan> Plans => config.GetSection("Subscriptions:Plans").GetChildren().Select(p=>new SubscriptionPlan(
        p.Key,p["Name"]??p.Key,p["Description"]??"Full construction workspace access",p["Interval"]??"month",p.GetValue<decimal>("Amount"),
        (p["Currency"]??"USD").ToUpperInvariant(),p["StripePriceId"],p["PayPalPlanId"]))
        .Where(p=>p.Key.Length<=60 && p.Name.Length<=120 && p.Amount>0 && decimal.Round(p.Amount,2)==p.Amount && p.Interval is "month" or "year" && p.Currency is "USD" or "EUR" or "GBP" or "CAD" or "AUD").ToList();
    public List<SubscriptionPlanDto> PublicPlans => Plans.Select(p=>new SubscriptionPlanDto(p.Key,p.Name,p.Description,p.Interval,p.Amount,p.Currency,
        Configured("Stripe") && p.StripePriceId?.StartsWith("price_",StringComparison.Ordinal)==true,
        Configured("PayPal") && p.PayPalPlanId?.StartsWith("P-",StringComparison.Ordinal)==true)).ToList();
    public string ReturnUrl {
        get {
            var value=config["Subscriptions:WebBaseUrl"];
            if(!Uri.TryCreate(value,UriKind.Absolute,out var u) || u.Scheme!="https" || u.UserInfo!="" || u.Query!="" || u.Fragment!="" || u.AbsolutePath!="/")
                throw new PaymentUnavailableException("The SaaS owner must configure the subscription website address.");
            return u.GetLeftPart(UriPartial.Authority)+"/Subscription/Index";
        }
    }
    public bool HasAccess(CompanySubscription? subscription, DateTime now) => !Enabled || (CanGrantAccess && subscription!=null && subscription.IsSandbox==Sandbox &&
        subscription.Status is "Active" or "Cancelled" && subscription.PaidThroughUtc>now);
    public static bool CanReplace(CompanySubscription? s, DateTime now) => s==null || (s.Status is "Cancelled" or "Expired" && !(s.PaidThroughUtc>now));
}
