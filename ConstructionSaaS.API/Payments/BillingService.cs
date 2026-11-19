using System.Security.Cryptography;
using System.Text;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Payments;

public class BillingService(ApplicationDbContext db, IPaymentGateway gateway, IDataProtectionProvider protection, IConfiguration config)
{
    private IDataProtector Protector => protection.CreateProtector("ConstructionSaaS.InvoiceLinks.v1");
    public IQueryable<ProjectInvoice> Query => db.ProjectInvoices.Include(i => i.Project).ThenInclude(p => p.Company).Include(i => i.Lines);
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public Task<ProjectInvoice?> FindLink(string token)
        => token.Length is < 40 or > 100 ? Task.FromResult<ProjectInvoice?>(null) : Query.FirstOrDefaultAsync(i => i.LinkHash == Hash(token));
    public string WebBase()
    {
        var value = config["Payments:WebBaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || uri.Query != "" || uri.Fragment != "" || uri.AbsolutePath != "/")
            throw new PaymentUnavailableException("Configure the trusted HTTPS payment website address first.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public void SetLink(ProjectInvoice invoice)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        invoice.LinkHash = Hash(token); invoice.ProtectedLink = Protector.Protect(token);
    }
    public string PaymentUrl(ProjectInvoice invoice) => WebBase() + "/Billing/Pay?link=" + Uri.EscapeDataString(Protector.Unprotect(invoice.ProtectedLink));
    public InvoiceDto Dto(ProjectInvoice invoice, bool admin = false) => new() {
        Id = invoice.Id, ProjectId = invoice.ProjectId, ProjectName = invoice.Project.Name, CompanyName = invoice.Project.Company!.Name,
        Number = invoice.Number, ClientName = invoice.ClientName, Currency = invoice.Currency, Provider = invoice.Provider, IsSandbox = invoice.IsSandbox,
        Status = invoice.Status, Total = invoice.Total, DueDateUtc = invoice.DueDateUtc, PaidAtUtc = invoice.PaidAtUtc, PaymentReference = invoice.PaymentReference,
        PaymentUrl = admin ? PaymentUrl(invoice) : null,
        Lines = invoice.Lines.Select(l => new InvoiceLineInput { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList()
    };
    public static bool Matches(ProjectInvoice invoice, CheckoutSnapshot result) => result.InvoiceId == invoice.Id && result.Amount == invoice.Total && result.Currency == invoice.Currency &&
        (invoice.CheckoutId == null || invoice.CheckoutId == result.Id);
    public async Task<CheckoutSnapshot> Checkout(ProjectInvoice invoice)
    {
        CheckMode(invoice);
        if (invoice.Status != "Open") throw new PaymentUnavailableException("This invoice is not open for payment.");
        if (invoice.CheckoutId != null) return await Refresh(invoice, false);
        // Persist before the external call. Retrying a lost response uses the same provider idempotency key.
        if (invoice.CheckoutStartedAtUtc == null) {
            invoice.CheckoutStartedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        // PayPal's default retention is six hours; never create another charge after that window.
        if (invoice.CheckoutStartedAtUtc < DateTime.UtcNow.AddHours(-5))
            throw new PaymentUnavailableException("The previous checkout needs reconciliation. Contact the company before making another payment.");
        var result = await gateway.Create(invoice, PaymentUrl(invoice));
        await Apply(invoice, result);
        return result;
    }
    public async Task<CheckoutSnapshot> Refresh(ProjectInvoice invoice, bool capture)
    {
        CheckMode(invoice);
        if (invoice.CheckoutId == null) throw new PaymentUnavailableException("Checkout has not started yet.");
        var result = await gateway.Read(invoice, capture);
        await Apply(invoice, result);
        return result;
    }
    private void CheckMode(ProjectInvoice invoice)
    {
        if (invoice.IsSandbox != gateway.Sandbox(invoice.Project.CompanyId, invoice.Provider))
            throw new PaymentUnavailableException("This invoice belongs to a different payment environment. Contact the company.");
    }
    public async Task Apply(ProjectInvoice invoice, CheckoutSnapshot result)
    {
        if (!Matches(invoice, result) || string.IsNullOrWhiteSpace(result.Id)) throw new PaymentUnavailableException("Payment verification failed: invoice, currency or amount does not match.");
        if (result.Status == "Paid" && string.IsNullOrWhiteSpace(result.Reference)) throw new PaymentUnavailableException("Payment confirmation is incomplete.");
        if (invoice.Status == "Voided") throw new PaymentUnavailableException("Voided invoice received an unexpected payment. Manual review is required.");
        if (result.Url != null && !PaymentGateway.IsCheckoutUrl(invoice.Provider, result.Url)) throw new PaymentUnavailableException("Unexpected checkout address.");
        // A delayed request must not regress a confirmed payment to an unpaid state.
        var next = result.Status;
        if (invoice.Status is "Paid" or "Refunded" or "Review" && next is "Open" or "Expired" or "Failed") next = invoice.Status;
        if (invoice.Status == "Refunded" && next == "Paid") next = "Refunded";
        var changed = invoice.Status != next;
        invoice.CheckoutId = result.Id;
        if (result.Url != null) invoice.CheckoutUrl = result.Url;
        if (result.Reference != null) invoice.PaymentReference = result.Reference;
        invoice.Status = next;
        if (next == "Paid") invoice.PaidAtUtc ??= DateTime.UtcNow;
        if (changed) db.WorkspaceActivities.Add(new WorkspaceActivity {
            CompanyId = invoice.Project.CompanyId, ProjectId = invoice.ProjectId, Category = "Payments", Actor = invoice.Provider + " verification",
            Description = $"Invoice {invoice.Number}: {next} ({invoice.Currency} {invoice.Total:0.00})."
        });
        await db.SaveChangesAsync();
    }
}
