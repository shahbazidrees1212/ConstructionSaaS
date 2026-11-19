using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ConstructionSaaS.API.Payments;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

static class BillingChecks
{
    public static async Task RunAsync(IServiceProvider services, string url, string adminToken, string otherToken, int company, int foreignProject, string engineerId, Action<bool,string> check)
    {
        var fake = services.GetRequiredService<FakeGateway>();
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        using var publicClient = new HttpClient { BaseAddress = new Uri(url) };
        void Use(string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using (var noAuth = await publicClient.GetAsync("/api/billing/invoices")) check(noAuth.StatusCode == HttpStatusCode.Unauthorized, "Billing requires authentication");
        int projectId;
        await using (var scope = services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var project = new Project { CompanyId = company, ProjectCode = "BILLING", Name = "Billing test project", Country = "PK", CurrencyCode = "USD" };
            db.Projects.Add(project); await db.SaveChangesAsync(); projectId = project.Id;
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(engineerId))!;
            Use(scope.ServiceProvider.GetRequiredService<ITokenService>().CreateToken(user.Id, user.Email!, new[] { "SiteEngineer" }, user.CompanyId, user.SecurityStamp));
        }
        using (var forbidden = await client.GetAsync("/api/billing/invoices")) check(forbidden.StatusCode == HttpStatusCode.Forbidden, "Site engineer cannot access financial administration");
        Use(adminToken);
        InvoiceInput Input(string number, string provider = "Stripe") => new() { ProjectId = projectId, Number = number, ClientName = "Client test", ClientEmail = "client@example.test", Currency = "USD", Provider = provider, DueDateUtc = DateTime.UtcNow.AddDays(10), Lines = [new() { Description = "Foundation milestone", Quantity = 2, UnitPrice = 25.50m }] };
        var invalid = Input("BAD"); invalid.Currency = "PKR";
        using (var response = await client.PostAsJsonAsync("/api/billing/invoices", invalid)) check(response.StatusCode == HttpStatusCode.BadRequest, "Unsupported billing currency rejected, not silently converted");
        invalid = Input("FOREIGN"); invalid.ProjectId = foreignProject;
        using (var response = await client.PostAsJsonAsync("/api/billing/invoices", invalid)) check(response.StatusCode == HttpStatusCode.NotFound, "Cannot issue invoice for foreign project");
        invalid = Input("PRECISION"); invalid.Lines[0].UnitPrice = 1.001m;
        using (var response = await client.PostAsJsonAsync("/api/billing/invoices", invalid)) check(response.StatusCode == HttpStatusCode.BadRequest, "Excess currency precision rejected");
        fake.Enabled = false;
        using (var response = await client.PostAsJsonAsync("/api/billing/invoices", Input("DISABLED"))) check(response.StatusCode == HttpStatusCode.Conflict, "Unconfigured merchant cannot issue payment invoice");
        fake.Enabled = true;
        async Task<InvoiceDto> Create(string number, string provider = "Stripe") {
            using var response = await client.PostAsJsonAsync("/api/billing/invoices", Input(number, provider));
            check(response.StatusCode == HttpStatusCode.Created, "Create " + provider + " invoice " + number);
            var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            return (await client.GetFromJsonAsync<InvoiceDto>("/api/billing/invoices/" + id))!;
        }
        var invoice = await Create("INV-001");
        check(invoice.Total == 51m && invoice.IsSandbox && invoice.Status == "Open", "Invoice total is computed on server and marked sandbox");
        using (var duplicate = await client.PostAsJsonAsync("/api/billing/invoices", Input("INV-001"))) check(duplicate.StatusCode == HttpStatusCode.Conflict, "Duplicate invoice number returns 409");
        Use(otherToken);
        using (var foreign = await client.GetAsync("/api/billing/invoices/" + invoice.Id)) check(foreign.StatusCode == HttpStatusCode.NotFound, "Other company cannot read payment link");
        using (var foreign = await client.PostAsync($"/api/billing/invoices/{invoice.Id}/void", null)) check(foreign.StatusCode == HttpStatusCode.NotFound, "Other company cannot void invoice");
        Use(adminToken);
        static string Link(InvoiceDto i) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(i.PaymentUrl!).Query)["link"].ToString();
        var link = Link(invoice);
        using (var bad = await publicClient.GetAsync("/api/payments/invoice?link=" + new string('x',43))) check(bad.StatusCode == HttpStatusCode.NotFound, "Guessed payment link rejected");
        using (var read = await publicClient.GetAsync("/api/payments/invoice?link=" + link)) {
            var text = await read.Content.ReadAsStringAsync();
            check(read.IsSuccessStatusCode && !text.Contains("client@example.test") && !text.Contains("protectedLink"), "Public invoice exposes no email or stored link secrets");
        }
        using (var confirm = await publicClient.PostAsJsonAsync("/api/payments/confirm", new { link })) check((await confirm.Content.ReadFromJsonAsync<InvoiceDto>())!.Status == "Open", "Return confirmation alone does not mark payment paid");
        using (var start = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link })) check(start.IsSuccessStatusCode, "Create hosted checkout");
        using (var retry = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link })) check(retry.IsSuccessStatusCode && fake.Creates[invoice.Id] == 1, "Repeated checkout reuses provider session");
        using (var voidUsed = await client.PostAsync($"/api/billing/invoices/{invoice.Id}/void", null)) check(voidUsed.StatusCode == HttpStatusCode.Conflict, "Cannot void active checkout");
        var snapshot = fake.Sessions[invoice.Id];
        fake.Sessions[invoice.Id] = snapshot with { Status = "Paid", Reference = "pi_test", Amount = 1m };
        using (var mismatch = await publicClient.PostAsJsonAsync("/api/payments/confirm", new { link })) check(mismatch.StatusCode == HttpStatusCode.ServiceUnavailable, "Underpayment is not accepted");
        fake.Sessions[invoice.Id] = snapshot with { Status = "Paid", Reference = "pi_test", Currency = "EUR" };
        using (var mismatch = await publicClient.PostAsJsonAsync("/api/payments/confirm", new { link })) check(mismatch.StatusCode == HttpStatusCode.ServiceUnavailable, "Wrong payment currency is not accepted");
        fake.Sessions[invoice.Id] = snapshot with { Status = "Paid", Reference = "pi_test" };
        var eventBody = JsonSerializer.Serialize(new { type = "checkout.session.completed", data = new { @object = new { id = snapshot.Id, client_reference_id = invoice.Id.ToString("N") } } });
        using (var forged = await publicClient.PostAsync($"/api/payments/webhooks/stripe/{company}", new StringContent(eventBody, Encoding.UTF8, "application/json"))) check(forged.StatusCode == HttpStatusCode.BadRequest, "Unsigned webhook rejected");
        publicClient.DefaultRequestHeaders.Add("X-Fake-Verified", "yes");
        using (var foreignHook = await publicClient.PostAsync($"/api/payments/webhooks/stripe/{company + 10000}", new StringContent(eventBody, Encoding.UTF8, "application/json")))
            check(foreignHook.IsSuccessStatusCode && (await client.GetFromJsonAsync<InvoiceDto>("/api/billing/invoices/" + invoice.Id))!.Status == "Open", "Webhook cannot settle an invoice through another company route");
        using (var webhook = await publicClient.PostAsync($"/api/payments/webhooks/stripe/{company}", new StringContent(eventBody, Encoding.UTF8, "application/json"))) check(webhook.IsSuccessStatusCode, "Verified webhook reconciles payment");
        using (var repeated = await publicClient.PostAsync($"/api/payments/webhooks/stripe/{company}", new StringContent(eventBody, Encoding.UTF8, "application/json"))) check(repeated.IsSuccessStatusCode, "Duplicate webhook acknowledged idempotently");
        var paid = await client.GetFromJsonAsync<InvoiceDto>("/api/billing/invoices/" + invoice.Id);
        check(paid!.Status == "Paid" && paid.PaidAtUtc.HasValue && paid.PaymentReference == "pi_test", "Verified payment reference and timestamp saved");
        await using (var scope = services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            check(await db.WorkspaceActivities.CountAsync(a => a.ProjectId == projectId && a.Description.Contains("INV-001: Paid")) == 1, "Duplicate webhook creates no duplicate paid audit entry");
        }
        using (var secondPayment = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link })) check(secondPayment.StatusCode == HttpStatusCode.ServiceUnavailable, "Paid invoice cannot start a second payment");
        fake.Sessions[invoice.Id] = snapshot with { Status = "Refunded", Reference = "pi_test" };
        using (var refund = await client.PostAsync($"/api/billing/invoices/{invoice.Id}/refresh", null)) check((await refund.Content.ReadFromJsonAsync<InvoiceDto>())!.Status == "Refunded", "Provider refund reconciles without deleting payment history");
        var paypal = await Create("INV-PP", "PayPal"); var ppLink = Link(paypal);
        using (var start = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link = ppLink })) check(start.IsSuccessStatusCode, "PayPal checkout is available");
        fake.PayPalApproved = true;
        using (var capture = await publicClient.PostAsJsonAsync("/api/payments/confirm", new { link = ppLink })) check((await capture.Content.ReadFromJsonAsync<InvoiceDto>())!.Status == "Paid", "PayPal confirmation captures stored order before marking paid");
        fake.PayPalApproved = false;
        fake.Sessions[paypal.Id] = fake.Sessions[paypal.Id] with { Status = "Refunded" };
        var refundEvent = new { event_type = "PAYMENT.CAPTURE.REFUNDED", resource = new { id = "REFUND-1", links = new[] { new { rel = "up", href = "https://api-m.sandbox.paypal.com/v2/payments/captures/paypal-capture-test" } } } };
        using (var refund = await publicClient.PostAsJsonAsync($"/api/payments/webhooks/paypal/{company}", refundEvent))
            check(refund.IsSuccessStatusCode && (await client.GetFromJsonAsync<InvoiceDto>("/api/billing/invoices/" + paypal.Id))!.Status == "Refunded", "PayPal refund webhook reconciles its original capture");
        var unused = await Create("INV-VOID");
        using (var cancel = await client.PostAsync($"/api/billing/invoices/{unused.Id}/void", null)) check(cancel.StatusCode == HttpStatusCode.NoContent, "Void unused invoice");
        using (var payVoid = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link = Link(unused) })) check(payVoid.StatusCode == HttpStatusCode.ServiceUnavailable, "Voided invoice cannot be paid");
        using (var delete = await client.DeleteAsync("/api/projects/" + projectId)) check(delete.StatusCode == HttpStatusCode.Conflict, "Project deletion preserves financial records with 409");
        var concurrent = await Create("INV-RACE");
        var race = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => publicClient.PostAsJsonAsync("/api/payments/checkout", new { link = Link(concurrent) })));
        check(race.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict) && race.Any(r => r.IsSuccessStatusCode), "Concurrent checkout requests succeed or return retryable 409, never 500");
        foreach (var response in race) response.Dispose();
        fake.Sessions[concurrent.Id] = fake.Sessions[concurrent.Id] with { Status = "Expired" };
        using (var expired = await client.PostAsync($"/api/billing/invoices/{concurrent.Id}/refresh", null)) check((await expired.Content.ReadFromJsonAsync<InvoiceDto>())!.Status == "Expired", "Expired provider session is recorded");
        using (var expiredPay = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link = Link(concurrent) })) check(expiredPay.StatusCode == HttpStatusCode.ServiceUnavailable, "Expired invoice does not create a second checkout");
        var uncertain = await Create("INV-UNCERTAIN");
        await using (var scope = services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.ProjectInvoices.SingleAsync(i => i.Id == uncertain.Id);
            row.CheckoutStartedAtUtc = DateTime.UtcNow.AddHours(-7); await db.SaveChangesAsync();
        }
        using (var retryLate = await publicClient.PostAsJsonAsync("/api/payments/checkout", new { link = Link(uncertain) }))
            check(retryLate.StatusCode == HttpStatusCode.ServiceUnavailable && !fake.Creates.ContainsKey(uncertain.Id), "Lost checkout response outside idempotency window cannot create duplicate order");
        SignatureChecks(check);
        await PaymentProviderChecks.RunAsync(check);
    }
    private static void SignatureChecks(Action<bool,string> check)
    {
        const string body = "{\"id\":\"evt_test\"}", secret = "whsec_test_only";
        var now = DateTimeOffset.UtcNow; var timestamp = now.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
        var header = $"t={timestamp},v1={signature}";
        check(PaymentGateway.VerifyStripeSignature(body, header, secret, now), "Real Stripe HMAC accepts authentic raw payload");
        check(!PaymentGateway.VerifyStripeSignature(body + " ", header, secret, now), "Stripe HMAC rejects modified body");
        check(!PaymentGateway.VerifyStripeSignature(body, header, secret, now.AddMinutes(6)), "Stripe HMAC rejects replay outside timestamp tolerance");
        check(!PaymentGateway.VerifyStripeSignature(body, "t=9223372036854775807,v1=00", secret, now), "Stripe signature handles extreme timestamp safely");
        check(!PaymentGateway.IsCheckoutUrl("Stripe", "https://checkout.stripe.com.evil.test/x") && !PaymentGateway.IsCheckoutUrl("PayPal", "https://www.paypal.com@evil.test"), "Checkout redirects reject lookalike domains");
    }
    public sealed class FakeGateway : IPaymentGateway
    {
        public bool Enabled { get; set; } = true;
        public bool PayPalApproved { get; set; }
        public ConcurrentDictionary<Guid, CheckoutSnapshot> Sessions { get; } = new();
        public ConcurrentDictionary<Guid,int> Creates { get; } = new();
        public bool Configured(int company, string provider) => Enabled;
        public bool Sandbox(int company, string provider) => true;
        public Task<CheckoutSnapshot> Create(ProjectInvoice i, string url) {
            Creates.AddOrUpdate(i.Id,1,(_,n) => n+1);
            return Task.FromResult(Sessions.GetOrAdd(i.Id, _ => new(i.Id.ToString("N"), i.Provider == "Stripe" ? "https://checkout.stripe.com/test" : "https://www.sandbox.paypal.com/checkoutnow?token=test", "Open", null, i.Total, i.Currency, i.Id)));
        }
        public Task<CheckoutSnapshot> Read(ProjectInvoice i, bool capture) {
            if (i.Provider == "PayPal" && capture && PayPalApproved) Sessions[i.Id] = Sessions[i.Id] with { Status = "Paid", Reference = "paypal-capture-test" };
            return Task.FromResult(Sessions[i.Id]);
        }
        public Task<bool> VerifyWebhook(int company, string provider, string body, IHeaderDictionary headers) => Task.FromResult(headers["X-Fake-Verified"] == "yes");
    }
}
