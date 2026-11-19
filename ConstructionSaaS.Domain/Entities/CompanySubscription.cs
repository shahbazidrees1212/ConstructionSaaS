namespace ConstructionSaaS.Domain.Entities;

public class CompanySubscription
{
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public Guid AttemptId { get; set; } = Guid.NewGuid();
    public string PlanKey { get; set; } = "";
    public string PlanName { get; set; } = "";
    public string Interval { get; set; } = "month";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Provider { get; set; } = "";
    public string ProviderPlanId { get; set; } = "";
    public bool IsSandbox { get; set; }
    public string Status { get; set; } = "Pending";
    public string? CheckoutId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? ProviderSubscriptionId { get; set; }
    public string? CustomerId { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidThroughUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public byte[] Version { get; set; } = [];
}

public class SubscriptionPayment
{
    public long Id { get; set; }
    public int CompanyId { get; set; }
    public Company Company { get; set; } = null!;
    public string Provider { get; set; } = "";
    public bool IsSandbox { get; set; }
    public string Reference { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public DateTime PaidAtUtc { get; set; }
}
