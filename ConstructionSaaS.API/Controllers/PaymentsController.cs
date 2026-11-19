using System.Text.Json;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.Application.DTOs.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;

[ApiController, AllowAnonymous, Route("api/payments"), TypeFilter(typeof(PaymentExceptionFilter))]
public class PaymentsController(BillingService billing, IPaymentGateway gateway) : Controller
{
    // Possession of a 256-bit random link grants access to this invoice only, never workspace data.
    [HttpGet("invoice"), EnableRateLimiting("payments")]
    public async Task<IActionResult> Invoice(string link)
    {
        var invoice = await billing.FindLink(link);
        return invoice == null ? NotFound() : Ok(billing.Dto(invoice));
    }
    [HttpPost("checkout"), EnableRateLimiting("payments")]
    public async Task<IActionResult> Checkout([FromBody] LinkInput input)
    {
        var invoice = await billing.FindLink(input.Link);
        if (invoice == null) return NotFound();
        var result = await billing.Checkout(invoice);
        if (result.Status != "Open" || !PaymentGateway.IsCheckoutUrl(invoice.Provider, result.Url)) return Conflict(new { message = "Checkout is no longer open. Refresh the invoice to see its payment status." });
        return Ok(new PaymentLinkDto(result.Url!));
    }
    [HttpPost("confirm"), EnableRateLimiting("payments")]
    public async Task<IActionResult> Confirm([FromBody] LinkInput input)
    {
        var invoice = await billing.FindLink(input.Link);
        if (invoice == null) return NotFound();
        if (invoice.CheckoutId != null && invoice.Status is not ("Voided" or "Expired")) await billing.Refresh(invoice, true);
        return Ok(billing.Dto(invoice));
    }
    [HttpPost("webhooks/{provider}/{company:int}"), RequestSizeLimit(262144)]
    public async Task<IActionResult> Webhook(string provider, int company)
    {
        provider = provider.ToLowerInvariant() switch { "stripe" => "Stripe", "paypal" => "PayPal", _ => "" };
        if (company <= 0 || provider == "") return NotFound();
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        if (!gateway.Configured(company, provider)) return StatusCode(503);
        if (!await gateway.VerifyWebhook(company, provider, body, Request.Headers)) return BadRequest();
        JsonElement data;
        try { data = JsonSerializer.Deserialize<JsonElement>(body); } catch (JsonException) { return BadRequest(); }
        string? checkoutId = null, reference = null;
        Guid? invoiceId = null;
        if (provider == "Stripe") {
            var type = data.GetProperty("type").GetString() ?? "";
            if (!(type.StartsWith("checkout.session.") || type is "charge.refunded" or "charge.dispute.created" or "charge.dispute.closed")) return Ok();
            var resource = data.GetProperty("data").GetProperty("object");
            if (type.StartsWith("checkout.session.")) {
                checkoutId = resource.GetProperty("id").GetString();
                if (resource.TryGetProperty("client_reference_id", out var clientRef) && Guid.TryParse(clientRef.GetString(), out var parsed)) invoiceId = parsed;
            } else if (resource.TryGetProperty("payment_intent", out var intent)) reference = intent.GetString();
        } else {
            var type = data.GetProperty("event_type").GetString() ?? "";
            if (!(type == "CHECKOUT.ORDER.APPROVED" || type.StartsWith("PAYMENT.CAPTURE."))) return Ok();
            var resource = data.GetProperty("resource");
            if (type == "CHECKOUT.ORDER.APPROVED") {
                checkoutId = resource.GetProperty("id").GetString();
                if (resource.TryGetProperty("purchase_units", out var units) && units.GetArrayLength() == 1 && units[0].TryGetProperty("reference_id", out var rid) && Guid.TryParse(rid.GetString(), out var parsed)) invoiceId = parsed;
            } else {
                reference = resource.GetProperty("id").GetString();
                if (resource.TryGetProperty("custom_id", out var custom) && Guid.TryParse(custom.GetString(), out var parsed)) invoiceId = parsed;
                if (resource.TryGetProperty("supplementary_data", out var extra) && extra.TryGetProperty("related_ids", out var ids)) {
                    if (ids.TryGetProperty("order_id", out var oid)) checkoutId = oid.GetString();
                    if (ids.TryGetProperty("capture_id", out var cid)) reference = cid.GetString();
                }
                // Refund events may identify the original capture only through the parent link.
                // Extract its ID, but never request a URL supplied by the webhook body.
                if (type == "PAYMENT.CAPTURE.REFUNDED" && resource.TryGetProperty("links", out var links)) {
                    foreach (var entry in links.EnumerateArray()) {
                        if (entry.GetProperty("rel").GetString() != "up" || !Uri.TryCreate(entry.GetProperty("href").GetString(), UriKind.Absolute, out var parent)) continue;
                        var match = System.Text.RegularExpressions.Regex.Match(parent.AbsolutePath, @"^/v2/payments/captures/([A-Za-z0-9_-]{1,255})$");
                        if (match.Success) reference = match.Groups[1].Value;
                    }
                }
            }
        }
        var invoice = await billing.Query.FirstOrDefaultAsync(i => i.Project.CompanyId == company && i.Provider == provider &&
            ((checkoutId != null && i.CheckoutId == checkoutId) || (reference != null && i.PaymentReference == reference) || (invoiceId != null && i.Id == invoiceId)));
        if (invoice == null) return Ok(); // Event for another integration on the merchant account.
        if (invoice.CheckoutId == null) {
            if (checkoutId == null || invoice.CheckoutStartedAtUtc == null) return StatusCode(503);
            invoice.CheckoutId = checkoutId; // Stored only after authoritative amount/currency/merchant verification.
        }
        await billing.Refresh(invoice, provider == "PayPal");
        return Ok();
    }
    public class LinkInput
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 40)]
        public string Link { get; set; } = "";
    }
}
