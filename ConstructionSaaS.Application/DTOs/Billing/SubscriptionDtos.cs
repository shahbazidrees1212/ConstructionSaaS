using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Billing;

public record SubscriptionPlanDto(string Key, string Name, string Description, string Interval, decimal Amount, string Currency, bool Stripe, bool PayPal);
public record SubscriptionPaymentDto(string Provider, string Reference, decimal Amount, string Currency, DateTime PaidAtUtc, bool IsSandbox);
public class SubscriptionOverviewDto
{
    public bool BillingEnabled { get; set; }
    public bool IsAdmin { get; set; }
    public bool HasAccess { get; set; }
    public bool IsSandbox { get; set; }
    public string Status { get; set; } = "Not subscribed";
    public string? PlanName { get; set; }
    public string? Provider { get; set; }
    public string? Interval { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime? PaidThroughUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public bool CanSubscribe { get; set; }
    public bool CanCancel { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; } = [];
    public List<SubscriptionPaymentDto> Payments { get; set; } = [];
}
public class SubscribeInput
{
    [Required, StringLength(60)] public string PlanKey { get; set; } = "";
    [Required, RegularExpression("^(Stripe|PayPal)$")] public string Provider { get; set; } = "";
}
