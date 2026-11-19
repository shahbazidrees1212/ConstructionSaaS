using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.API.Payments;

public record CheckoutSnapshot(string Id, string? Url, string Status, string? Reference, decimal Amount, string Currency, Guid InvoiceId);
public sealed class PaymentUnavailableException(string message) : Exception(message);
public interface IPaymentGateway
{
    bool Configured(int company, string provider);
    bool Sandbox(int company, string provider);
    Task<CheckoutSnapshot> Create(ProjectInvoice invoice, string returnUrl);
    Task<CheckoutSnapshot> Read(ProjectInvoice invoice, bool capture);
    Task<bool> VerifyWebhook(int company, string provider, string body, IHeaderDictionary headers);
}

// Calls are server-to-server. Card details never pass through this application.
public class PaymentGateway(IHttpClientFactory factory, IConfiguration config, ILogger<PaymentGateway>? logger = null) : IPaymentGateway
{
    private string? Setting(int company, string key) => config[$"Payments:Merchants:{company}:{key}"];
    public bool Sandbox(int company, string provider) => !string.Equals(Setting(company, provider + ":Mode"), "Live", StringComparison.Ordinal);
    public bool Configured(int company, string provider)
    {
        if (provider is not ("Stripe" or "PayPal")) return false;
        var mode = Setting(company, provider + ":Mode");
        if (mode is not ("Sandbox" or "Live")) return false;
        if (provider == "PayPal") return new[] { "ClientId", "ClientSecret", "WebhookId", "MerchantId" }.All(k => !string.IsNullOrWhiteSpace(Setting(company, "PayPal:" + k)));
        var key = Setting(company, "Stripe:SecretKey");
        return !string.IsNullOrWhiteSpace(Setting(company, "Stripe:WebhookSecret")) && key != null &&
            key.StartsWith(Sandbox(company, provider) ? "sk_test_" : "sk_live_", StringComparison.Ordinal);
    }
    private async Task<HttpClient> Client(int company, string provider)
    {
        if (!Configured(company, provider)) throw new PaymentUnavailableException("This company's payment account is not configured yet.");
        var client = factory.CreateClient("payments");
        if (provider == "Stripe") {
            client.BaseAddress = new Uri("https://api.stripe.com/v1/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Setting(company, "Stripe:SecretKey"));
            client.DefaultRequestHeaders.Add("Stripe-Version", "2026-10-28.endive");
        } else {
            client.BaseAddress = new Uri(Sandbox(company, provider) ? "https://api-m.sandbox.paypal.com/" : "https://api-m.paypal.com/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Setting(company, "PayPal:ClientId") + ":" + Setting(company, "PayPal:ClientSecret"))));
            try {
                using var result = await client.PostAsync("v1/oauth2/token", new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "client_credentials" }));
                var token = await Body(result);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());
            } catch { client.Dispose(); throw; }
        }
        return client;
    }
    private async Task<JsonElement> Body(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) {
            // Never log the response body, authorization headers or checkout URLs: they can contain secrets or customer data.
            var provider = response.RequestMessage?.RequestUri?.Host == "api.stripe.com" ? "Stripe" : "Payment provider";
            var requestId = response.Headers.TryGetValues("Request-Id", out var ids) ? ids.FirstOrDefault() : null;
            logger?.LogWarning("{Provider} rejected a payment request. HTTP {Status}; request ID {RequestId}", provider, (int)response.StatusCode, requestId);
            var reason = response.StatusCode switch {
                System.Net.HttpStatusCode.Unauthorized => "The payment provider rejected the company's API credentials. Contact the company to check its payment configuration.",
                System.Net.HttpStatusCode.Forbidden => "The company's payment account does not have permission for this request. Contact the company.",
                System.Net.HttpStatusCode.BadRequest => "The payment provider rejected the checkout details. Contact the company to review the request in its provider dashboard.",
                System.Net.HttpStatusCode.TooManyRequests => "The payment provider is busy. Wait before checking payment status again.",
                _ => "The payment provider could not complete this request. Contact the company or check payment status later."
            };
            throw new PaymentUnavailableException(reason + " Do not submit a second payment.");
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public async Task<CheckoutSnapshot> Create(ProjectInvoice invoice, string returnUrl)
    {
        using var client = await Client(invoice.Project.CompanyId, invoice.Provider);
        using var request = new HttpRequestMessage(HttpMethod.Post, invoice.Provider == "Stripe" ? "checkout/sessions" : "v2/checkout/orders");
        var key = invoice.Id.ToString("N");
        request.Headers.Add(invoice.Provider == "Stripe" ? "Idempotency-Key" : "PayPal-Request-Id", key);
        if (invoice.Provider == "Stripe") {
            request.Content = new FormUrlEncodedContent(new Dictionary<string,string> {
                // Payment methods are selected in the merchant's Stripe Dashboard.
                ["mode"] = "payment",
                ["client_reference_id"] = key, ["customer_email"] = invoice.ClientEmail,
                ["success_url"] = returnUrl, ["cancel_url"] = returnUrl,
                ["line_items[0][quantity]"] = "1", ["line_items[0][price_data][currency]"] = invoice.Currency.ToLowerInvariant(),
                ["line_items[0][price_data][unit_amount]"] = ((long)(invoice.Total * 100)).ToString(CultureInfo.InvariantCulture),
                ["line_items[0][price_data][product_data][name]"] = "Invoice " + invoice.Number,
                ["metadata[invoice_id]"] = key
            });
        } else {
            request.Headers.Add("Prefer", "return=representation");
            request.Content = JsonContent.Create(new {
                intent = "CAPTURE",
                purchase_units = new[] { new { reference_id = key, custom_id = key, description = "Invoice " + invoice.Number,
                    payee = new { merchant_id = Setting(invoice.Project.CompanyId, "PayPal:MerchantId") },
                    amount = new { currency_code = invoice.Currency, value = invoice.Total.ToString("0.00", CultureInfo.InvariantCulture) } } },
                payment_source = new { paypal = new { experience_context = new { return_url = returnUrl, cancel_url = returnUrl, user_action = "PAY_NOW", shipping_preference = "NO_SHIPPING" } } }
            });
        }
        using var response = await client.SendAsync(request);
        var body = await Body(response);
        return invoice.Provider == "Stripe" ? Stripe(body) : PayPal(body, invoice.Project.CompanyId);
    }
    public async Task<CheckoutSnapshot> Read(ProjectInvoice invoice, bool capture)
    {
        using var client = await Client(invoice.Project.CompanyId, invoice.Provider);
        var id = Uri.EscapeDataString(invoice.CheckoutId!);
        var route = invoice.Provider == "Stripe" ? "checkout/sessions/" + id + "?expand[]=payment_intent.latest_charge" : "v2/checkout/orders/" + id;
        using var response = await client.GetAsync(route);
        var body = await Body(response);
        if (invoice.Provider == "Stripe") return Stripe(body);
        // Approval is not payment. Capture only this invoice's stored order, never a browser-supplied ID.
        if (!BillingService.Matches(invoice, PayPal(body, invoice.Project.CompanyId)))
            throw new PaymentUnavailableException("The order does not match this invoice.");
        if (capture && body.GetProperty("status").GetString() == "APPROVED") {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders/" + id + "/capture") { Content = JsonContent.Create(new { }) };
            request.Headers.Add("PayPal-Request-Id", "capture-" + invoice.Id.ToString("N"));
            using var captured = await client.SendAsync(request);
            // Another webhook may have captured concurrently. Re-read authoritative state either way.
            using var refreshed = await client.GetAsync(route);
            body = await Body(refreshed);
        }
        var result = PayPal(body, invoice.Project.CompanyId);
        if (result.Reference != null) {
            using var payment = await client.GetAsync("v2/payments/captures/" + Uri.EscapeDataString(result.Reference));
            var detail = await Body(payment);
            var money = detail.GetProperty("amount");
            result = result with { Status = CaptureStatus(detail.GetProperty("status").GetString()), Amount = decimal.Parse(money.GetProperty("value").GetString()!, CultureInfo.InvariantCulture), Currency = money.GetProperty("currency_code").GetString()! };
        }
        return result;
    }
    private static string CaptureStatus(string? status) => status switch { "COMPLETED" => "Paid", "REFUNDED" => "Refunded", "PARTIALLY_REFUNDED" or "REVERSED" => "Review", "DECLINED" or "FAILED" => "Failed", _ => "Open" };
    private static CheckoutSnapshot Stripe(JsonElement body)
    {
        var status = body.GetProperty("payment_status").GetString() == "paid" ? "Paid" : body.GetProperty("status").GetString() == "expired" ? "Expired" : "Open";
        string? reference = null;
        if (body.TryGetProperty("payment_intent", out var pi)) {
            if (pi.ValueKind == JsonValueKind.String) reference = pi.GetString();
            if (pi.ValueKind == JsonValueKind.Object) {
                reference = pi.GetProperty("id").GetString();
                if (pi.TryGetProperty("latest_charge", out var charge) && charge.ValueKind == JsonValueKind.Object) {
                    if (charge.TryGetProperty("refunded", out var refunded) && refunded.GetBoolean()) status = "Refunded";
                    else if ((charge.TryGetProperty("amount_refunded", out var part) && part.GetInt64() > 0) || (charge.TryGetProperty("disputed", out var dispute) && dispute.GetBoolean())) status = "Review";
                }
            }
        }
        return new(body.GetProperty("id").GetString()!, body.TryGetProperty("url", out var url) ? url.GetString() : null, status, reference,
            body.GetProperty("amount_total").GetDecimal() / 100, body.GetProperty("currency").GetString()!.ToUpperInvariant(), Guid.Parse(body.GetProperty("client_reference_id").GetString()!));
    }
    private CheckoutSnapshot PayPal(JsonElement body, int company)
    {
        var units = body.GetProperty("purchase_units");
        if (units.GetArrayLength() != 1) throw new PaymentUnavailableException("Unexpected payment details; contact the company.");
        var unit = units[0];
        if (unit.GetProperty("payee").GetProperty("merchant_id").GetString() != Setting(company, "PayPal:MerchantId")) throw new PaymentUnavailableException("Payment merchant does not match this company.");
        var amount = unit.GetProperty("amount");
        string? reference = null;
        var state = body.GetProperty("status").GetString() == "VOIDED" ? "Expired" : "Open";
        if (unit.TryGetProperty("payments", out var payments) && payments.TryGetProperty("captures", out var captures) && captures.GetArrayLength() == 1) {
            reference = captures[0].GetProperty("id").GetString(); state = CaptureStatus(captures[0].GetProperty("status").GetString());
        }
        string? url = null;
        if (body.TryGetProperty("links", out var links)) foreach (var link in links.EnumerateArray())
            if (link.GetProperty("rel").GetString() is "payer-action" or "approve") url = link.GetProperty("href").GetString();
        return new(body.GetProperty("id").GetString()!, url, state, reference, decimal.Parse(amount.GetProperty("value").GetString()!, CultureInfo.InvariantCulture), amount.GetProperty("currency_code").GetString()!, Guid.Parse(unit.GetProperty("reference_id").GetString()!));
    }
    public async Task<bool> VerifyWebhook(int company, string provider, string body, IHeaderDictionary headers)
    {
        if (!Configured(company, provider)) return false;
        if (provider == "Stripe") return VerifyStripeSignature(body, headers["Stripe-Signature"].ToString(), Setting(company, "Stripe:WebhookSecret")!, DateTimeOffset.UtcNow);
        using var client = await Client(company, provider);
        foreach (var header in new[] { "PAYPAL-AUTH-ALGO", "PAYPAL-CERT-URL", "PAYPAL-TRANSMISSION-ID", "PAYPAL-TRANSMISSION-SIG", "PAYPAL-TRANSMISSION-TIME" })
            if (string.IsNullOrWhiteSpace(headers[header])) return false;
        using var response = await client.PostAsJsonAsync("v1/notifications/verify-webhook-signature", new {
            auth_algo = headers["PAYPAL-AUTH-ALGO"].ToString(), cert_url = headers["PAYPAL-CERT-URL"].ToString(),
            transmission_id = headers["PAYPAL-TRANSMISSION-ID"].ToString(), transmission_sig = headers["PAYPAL-TRANSMISSION-SIG"].ToString(), transmission_time = headers["PAYPAL-TRANSMISSION-TIME"].ToString(),
            webhook_id = Setting(company, "PayPal:WebhookId"), webhook_event = JsonSerializer.Deserialize<JsonElement>(body)
        });
        return (await Body(response)).GetProperty("verification_status").GetString() == "SUCCESS";
    }
    public static bool VerifyStripeSignature(string body, string header, string secret, DateTimeOffset now)
    {
        var values = header.Split(',').Select(s => s.Trim().Split('=', 2)).Where(s => s.Length == 2).ToList();
        var timestamps = values.Where(s => s[0] == "t").ToList();
        if (timestamps.Count != 1 || !long.TryParse(timestamps[0][1], out var timestamp) || Math.Abs((decimal)now.ToUnixTimeSeconds() - timestamp) > 300) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + body));
        foreach (var candidate in values.Where(s => s[0] == "v1")) {
            try { if (CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(candidate[1]))) return true; } catch (FormatException) { }
        }
        return false;
    }
    public static bool IsCheckoutUrl(string provider, string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort &&
           (provider == "Stripe" ? uri.Host == "checkout.stripe.com" : uri.Host is "www.paypal.com" or "www.sandbox.paypal.com");
}
