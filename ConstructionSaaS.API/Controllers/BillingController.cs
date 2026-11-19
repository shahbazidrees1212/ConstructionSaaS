using System.Security.Claims;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;

[ApiController, Route("api/billing"), Authorize(Roles = "CompanyAdmin")]
[TypeFilter(typeof(PaymentExceptionFilter))]
public class BillingController(ApplicationDbContext db, BillingService billing, IPaymentGateway gateway) : Controller
{
    private int CompanyId => int.TryParse(User.FindFirstValue("company_id"), out var id) ? id : 0;
    public override void OnActionExecuting(ActionExecutingContext context) { if (CompanyId <= 0) context.Result = Forbid(); }
    [HttpGet("providers")]
    public IActionResult Providers() => Ok(new[] { "Stripe", "PayPal" }.Select(p => new PaymentProviderDto(p, gateway.Configured(CompanyId, p), gateway.Sandbox(CompanyId, p))));
    [HttpGet("invoices")]
    public async Task<IActionResult> Invoices(int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        var invoices = await billing.Query.AsNoTracking().Where(i => i.Project.CompanyId == CompanyId).OrderByDescending(i => i.CreatedAtUtc).ThenBy(i => i.Id).Skip((page - 1) * 50).Take(50).ToListAsync();
        return Ok(invoices.Select(i => billing.Dto(i)));
    }
    [HttpGet("invoices/{id:guid}")]
    public async Task<IActionResult> Invoice(Guid id)
    {
        var invoice = await billing.Query.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.Project.CompanyId == CompanyId);
        return invoice == null ? NotFound() : Ok(billing.Dto(invoice, true));
    }
    [HttpPost("invoices")]
    public async Task<IActionResult> Create(InvoiceInput input)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == input.ProjectId && p.CompanyId == CompanyId);
        if (project == null) return NotFound();
        if (input.DueDateUtc.Date < DateTime.UtcNow.Date || input.DueDateUtc.Year > 2100) return BadRequest(new { message = "Choose a due date today or later, before 2101." });
        if (input.Lines == null || input.Lines.Any(l => l == null || l.UnitPrice != decimal.Round(l.UnitPrice, 2))) return BadRequest(new { message = "Use valid line items and prices with no more than two decimal places." });
        var total = input.Lines.Sum(l => l.Quantity * l.UnitPrice);
        if (total < 1 || total > 999999.99m) return BadRequest(new { message = "Invoice total must be between 1.00 and 999,999.99." });
        if (!gateway.Configured(CompanyId, input.Provider)) return Conflict(new { message = "Configure this company's merchant account before issuing a payment invoice." });
        _ = billing.WebBase();
        var invoice = new ProjectInvoice { ProjectId = project.Id, Number = input.Number.Trim(), ClientName = input.ClientName.Trim(), ClientEmail = input.ClientEmail.Trim(),
            Currency = input.Currency, Provider = input.Provider, IsSandbox = gateway.Sandbox(CompanyId, input.Provider), Total = total, DueDateUtc = input.DueDateUtc.Date,
            Lines = input.Lines.Select(l => new InvoiceLine { Description = l.Description.Trim(), Quantity = l.Quantity, UnitPrice = l.UnitPrice }).ToList() };
        billing.SetLink(invoice); db.ProjectInvoices.Add(invoice);
        db.WorkspaceActivities.Add(new WorkspaceActivity { CompanyId = CompanyId, ProjectId = project.Id, Category = "Payments", Actor = db.AuditActor, Description = "Issued invoice " + invoice.Number });
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { return Conflict(new { message = "This invoice number already exists on this project." }); }
        return CreatedAtAction(nameof(Invoice), new { id = invoice.Id }, new { invoice.Id });
    }
    [HttpPost("invoices/{id:guid}/refresh")]
    public async Task<IActionResult> Refresh(Guid id)
    {
        var invoice = await billing.Query.FirstOrDefaultAsync(i => i.Id == id && i.Project.CompanyId == CompanyId);
        if (invoice == null) return NotFound();
        if (invoice.CheckoutId != null) await billing.Refresh(invoice, false);
        return Ok(billing.Dto(invoice, true));
    }
    [HttpPost("invoices/{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id)
    {
        var invoice = await billing.Query.FirstOrDefaultAsync(i => i.Id == id && i.Project.CompanyId == CompanyId);
        if (invoice == null) return NotFound();
        if (invoice.Status != "Open" || invoice.CheckoutStartedAtUtc != null) return Conflict(new { message = "Only an unpaid invoice with no checkout started can be voided. Review other payments in the provider dashboard." });
        invoice.Status = "Voided";
        db.WorkspaceActivities.Add(new WorkspaceActivity { CompanyId = CompanyId, ProjectId = invoice.ProjectId, Category = "Payments", Actor = db.AuditActor, Description = "Voided invoice " + invoice.Number });
        await db.SaveChangesAsync(); return NoContent();
    }
}
