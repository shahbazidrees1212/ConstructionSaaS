namespace ConstructionSaaS.Domain.Entities;

// Issued invoices are immutable. Corrections require a new invoice, retaining the original record.
public class ProjectInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public string Number { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal Total { get; set; }
    public DateTime DueDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Provider { get; set; } = "";
    public bool IsSandbox { get; set; }
    public string Status { get; set; } = "Open";
    public string LinkHash { get; set; } = "";
    public string ProtectedLink { get; set; } = "";
    public string? CheckoutId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? CheckoutStartedAtUtc { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public byte[] Version { get; set; } = [];
    public List<InvoiceLine> Lines { get; set; } = [];
}

public class InvoiceLine
{
    public int Id { get; set; }
    public Guid InvoiceId { get; set; }
    public ProjectInvoice Invoice { get; set; } = null!;
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
