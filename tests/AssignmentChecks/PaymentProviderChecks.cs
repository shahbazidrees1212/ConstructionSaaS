using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

static class PaymentProviderChecks
{
    public static async Task RunAsync(Action<bool,string> check)
    {
        var settings = new Dictionary<string,string?> {
            ["Payments:Merchants:1:Stripe:Mode"] = "Sandbox", ["Payments:Merchants:1:Stripe:SecretKey"] = "sk_test_fixture", ["Payments:Merchants:1:Stripe:WebhookSecret"] = "whsec_fixture",
            ["Payments:Merchants:1:PayPal:Mode"] = "Sandbox", ["Payments:Merchants:1:PayPal:ClientId"] = "fixture-client", ["Payments:Merchants:1:PayPal:ClientSecret"] = "fixture-secret", ["Payments:Merchants:1:PayPal:WebhookId"] = "fixture-webhook", ["Payments:Merchants:1:PayPal:MerchantId"] = "fixture-merchant"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var invoice = new ProjectInvoice { Id = Guid.NewGuid(), Project = new Project { CompanyId = 1 }, Number = "INV-PROVIDER", ClientEmail = "fixture@example.test", Provider = "Stripe", Currency = "USD", Total = 51m, IsSandbox = true };
        bool captured = false;
        var requests = new List<(string Url, string Method, string Body, string? Key)>();
        var handler = new StubHandler(async request => {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            requests.Add((request.RequestUri.ToString(), request.Method.Method, body, request.Headers.TryGetValues("Idempotency-Key", out var sk) ? sk.Single() : request.Headers.TryGetValues("PayPal-Request-Id", out var pk) ? pk.Single() : null));
            object data;
            if (path == "/v1/oauth2/token") data = new { access_token = "fixture-access" };
            else if (path.Contains("verify-webhook-signature")) data = new { verification_status = "SUCCESS" };
            else if (path.EndsWith("/capture")) { captured = true; data = new { status = "COMPLETED" }; }
            else if (path.Contains("/v2/payments/captures/")) data = new { id = "CAPTURE-1", status = "COMPLETED", amount = new { currency_code = "USD", value = "51.00" } };
            else if (request.RequestUri.Host == "api.stripe.com") {
                data = new { id = "cs_test_fixture", url = "https://checkout.stripe.com/c/pay/test", payment_status = request.Method == HttpMethod.Post ? "unpaid" : "paid", status = request.Method == HttpMethod.Post ? "open" : "complete", amount_total = 5100, currency = "usd", client_reference_id = invoice.Id.ToString("N"), payment_intent = new { id = "pi_fixture", latest_charge = new { refunded = false, amount_refunded = 0, disputed = false } } };
            } else data = new { id = "ORDER-1", status = captured ? "COMPLETED" : "APPROVED", purchase_units = new[] { new { reference_id = invoice.Id.ToString("N"), payee = new { merchant_id = "fixture-merchant" }, amount = new { currency_code = "USD", value = "51.00" }, payments = new { captures = captured ? new[] { new { id = "CAPTURE-1", status = "COMPLETED" } } : [] } } }, links = new[] { new { rel = "payer-action", href = "https://www.sandbox.paypal.com/checkoutnow?token=ORDER-1" } } };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
        });
        var gateway = new PaymentGateway(new StubFactory(handler), config);
        check(gateway.Configured(1, "Stripe") && gateway.Configured(1, "PayPal") && !gateway.Configured(2,"Stripe"), "Merchant credentials are isolated by company");
        var stripe = await gateway.Create(invoice, "https://example.test/Billing/Pay?link=fixture");
        check(stripe.Amount == 51m && stripe.Status == "Open" && requests.Last().Body.Contains("unit_amount%5D=5100") && requests.Last().Key == invoice.Id.ToString("N"), "Stripe adapter submits server amount in minor units with idempotency key");
        check(!requests.Last().Body.Contains("payment_method_types"), "Stripe checkout uses dashboard-managed payment methods without the rejected legacy parameter");
        invoice.CheckoutId = stripe.Id;
        check((await gateway.Read(invoice, false)).Status == "Paid", "Stripe adapter verifies current session payment status");
        invoice.Provider = "PayPal"; invoice.CheckoutId = null;
        var paypal = await gateway.Create(invoice, "https://example.test/Billing/Pay?link=fixture");
        var order = requests.Last();
        check(order.Url.StartsWith("https://api-m.sandbox.paypal.com/") && order.Key == invoice.Id.ToString("N") && order.Body.Contains("fixture-merchant"), "PayPal adapter uses sandbox host, merchant binding and idempotency key");
        invoice.CheckoutId = paypal.Id;
        var settled = await gateway.Read(invoice, true);
        check(settled.Status == "Paid" && settled.Reference == "CAPTURE-1" && requests.Any(r => r.Method == "POST" && r.Url.EndsWith("/ORDER-1/capture") && r.Key == "capture-" + invoice.Id.ToString("N")), "PayPal adapter captures approved stored order and verifies capture");
        var headers = new HeaderDictionary();
        foreach (var name in new[] { "PAYPAL-AUTH-ALGO", "PAYPAL-CERT-URL", "PAYPAL-TRANSMISSION-ID", "PAYPAL-TRANSMISSION-SIG", "PAYPAL-TRANSMISSION-TIME" }) headers[name] = "fixture";
        check(await gateway.VerifyWebhook(1, "PayPal", "{\"id\":\"WH-fixture\"}", headers) && requests.Last().Body.Contains("fixture-webhook"), "PayPal webhook verification sends configured webhook ID to provider");
        config["Payments:Merchants:1:Stripe:Mode"] = "Live";
        check(!gateway.Configured(1,"Stripe"), "Stripe live mode rejects test secret key");
        config["Payments:Merchants:1:Stripe:Mode"] = "Sandbox";
        invoice.Provider = "Stripe";
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError }) {
            var rejected = new PaymentGateway(new StubFactory(new StubHandler(_ => Task.FromResult(
                new HttpResponseMessage(status) { Content = JsonContent.Create(new { error = new { message = "SECRET_RESPONSE_MUST_NOT_LEAK" } }) }))), config);
            try {
                await rejected.Create(invoice, "https://example.test/Billing/Pay?link=fixture");
                check(false, "Provider rejection must fail checkout");
            } catch (PaymentUnavailableException e) {
                check(!e.Message.Contains("SECRET_RESPONSE") && e.Message.Contains("Do not submit a second payment"), $"Provider HTTP {(int)status} returns safe payment failure without leaking response");
                if (status == HttpStatusCode.Unauthorized) check(e.Message.Contains("credentials"), "Invalid merchant credentials produce actionable error");
            }
        }
    }
    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class StubHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
}
