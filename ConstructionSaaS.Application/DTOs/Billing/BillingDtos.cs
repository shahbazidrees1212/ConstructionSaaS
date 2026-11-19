using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Billing;

public class InvoiceInput
{
    [Range(1, int.MaxValue)] public int ProjectId { get; set; }
    [Required, StringLength(40), RegularExpression(@"[A-Za-z0-9][A-Za-z0-9\-_/]{0,39}")] public string Number { get; set; } = "";
    [Required, StringLength(150)] public string ClientName { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string ClientEmail { get; set; } = "";
    [Required, RegularExpression("USD|EUR|GBP|CAD|AUD")] public string Currency { get; set; } = "";
    [Required, RegularExpression("Stripe|PayPal")] public string Provider { get; set; } = "";
    public DateTime DueDateUtc { get; set; } = DateTime.UtcNow.Date.AddDays(14);
    [MinLength(1), MaxLength(20)] public List<InvoiceLineInput> Lines { get; set; } = [new()];
}
public class InvoiceLineInput
{
    [Required, StringLength(250)] public string Description { get; set; } = "";
    [Range(1, 10000)] public int Quantity { get; set; } = 1;
    [Range(typeof(decimal), "0.01", "1000000")] public decimal UnitPrice { get; set; }
}
public class InvoiceDto
{
    public bool IsSandbox { get; set; }
    public Guid Id { get; set; }
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Number { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal Total { get; set; }
    public DateTime DueDateUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string? PaymentReference { get; set; }
    public string? PaymentUrl { get; set; }
    public List<InvoiceLineInput> Lines { get; set; } = [];
    public bool IsOverdue => Status == "Open" && DueDateUtc.Date < DateTime.UtcNow.Date;
}
public record PaymentLinkDto(string Url);
public record PaymentProviderDto(string Name, bool Configured, bool Sandbox);
