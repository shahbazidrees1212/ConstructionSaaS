using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Application.DTOs.Workspace;
namespace ConstructionSaaS.Web.Models;
public class BillingViewModel
{
    public InvoiceInput Input { get; set; } = new();
    public List<InvoiceDto> Invoices { get; set; } = [];
    public List<PaymentProviderDto> Providers { get; set; } = [];
    public WorkspaceReportDto Projects { get; set; } = new();
    public int Page { get; set; } = 1;
}
public class InvoicePaymentViewModel
{
    public InvoiceDto Invoice { get; set; } = new();
    public string Link { get; set; } = "";
}
