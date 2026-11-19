using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ConstructionSaaS.Application.DTOs.Billing;

static class BillingWebChecks
{
    public static async Task<(string Id, string Link)> RunAsync(HttpClient web, string apiUrl, string token, Action<bool,string> check)
    {
        using var api = new HttpClient { BaseAddress = new Uri(apiUrl) };
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using (var subscriptions = await web.GetAsync("/Subscription/Index")) {
            var html = await subscriptions.Content.ReadAsStringAsync();
            check(subscriptions.IsSuccessStatusCode && html.Contains("A plan for the way you build.") && html.Contains("Plans are being prepared"), "SaaS subscription page renders owner pricing setup state");
            check(subscriptions.Headers.GetValues("Content-Security-Policy").Single().Contains("https://billing.stripe.com") && subscriptions.Headers.GetValues("Referrer-Policy").Single()=="no-referrer","SaaS billing permits hosted checkout and portal redirects without leaking referrers");
        }
        using (var csrf = await web.PostAsync("/Subscription/Checkout", new FormUrlEncodedContent(new Dictionary<string,string>{{"PlanKey","monthly"},{"Provider","Stripe"}}))) check(csrf.StatusCode == HttpStatusCode.BadRequest,"SaaS checkout requires antiforgery token");
        var existing = (await api.GetFromJsonAsync<List<InvoiceDto>>("/api/billing/invoices"))!.First();
        using (var page = await web.GetAsync("/Billing/Index")) {
            var html = await page.Content.ReadAsStringAsync();
            check(page.IsSuccessStatusCode && html.Contains("Invoices &amp; payments") && html.Contains("INV-001"), "Billing list renders persisted invoices in admin shell");
        }
        var create = await web.GetStringAsync("/Billing/Create");
        check(create.Contains("billing.js") && create.Contains("Input.Lines[0].Description"), "Billing form renders invoice items and provider selector");
        using (var csrf = await web.PostAsync("/Billing/Create", new FormUrlEncodedContent(new Dictionary<string,string>()))) check(csrf.StatusCode == HttpStatusCode.BadRequest, "Invoice creation enforces antiforgery");
        var match = Regex.Match(create, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        var values = new Dictionary<string,string> {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value), ["Input.ProjectId"] = existing.ProjectId.ToString(), ["Input.Number"] = "INV-WEB",
            ["Input.ClientName"] = "Browser test client", ["Input.ClientEmail"] = "web@example.test", ["Input.Currency"] = "USD", ["Input.Provider"] = "Stripe", ["Input.DueDateUtc"] = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
            ["Input.Lines[0].Description"] = "Completed foundation work", ["Input.Lines[0].Quantity"] = "2", ["Input.Lines[0].UnitPrice"] = "125.50"
        };
        using var posted = await web.PostAsync("/Billing/Create", new FormUrlEncodedContent(values));
        check(posted.StatusCode == HttpStatusCode.Found && posted.Headers.Location!.ToString().Contains("Details"), "Invoice form creates persisted invoice through API");
        var detail = await web.GetStringAsync(posted.Headers.Location);
        check(detail.Contains("INV-WEB") && detail.Contains("251.00") && detail.Contains("Client payment link"), "Invoice detail displays calculated total and shareable link");
        var invoice = (await api.GetFromJsonAsync<List<InvoiceDto>>("/api/billing/invoices"))!.First(i => i.Number == "INV-WEB");
        invoice = (await api.GetFromJsonAsync<InvoiceDto>("/api/billing/invoices/" + invoice.Id))!;
        var link = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(invoice.PaymentUrl!).Query)["link"].ToString();
        using var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = web.BaseAddress };
        using (var forbidden = await anonymous.GetAsync("/Billing/Index")) check(forbidden.StatusCode == HttpStatusCode.Found, "Anonymous user cannot open billing workspace");
        using (var pay = await anonymous.GetAsync("/Billing/Pay?link=" + link)) {
            var html = await pay.Content.ReadAsStringAsync();
            check(pay.IsSuccessStatusCode && html.Contains("Pay with Stripe") && html.Contains("Test payment") && !html.Contains("sidebar"), "Client sees standalone sandbox checkout page");
            check(pay.Headers.GetValues("Content-Security-Policy").Single().Contains("https://checkout.stripe.com") && pay.Headers.GetValues("Referrer-Policy").Single() == "no-referrer", "Client page permits hosted payment redirect without leaking link in referrer");
        }
        using (var csrf = await anonymous.PostAsync("/Billing/Begin", new FormUrlEncodedContent(new Dictionary<string,string> { ["link"] = link }))) check(csrf.StatusCode == HttpStatusCode.BadRequest, "Public checkout form enforces antiforgery");
        return (invoice.Id.ToString(), link);
    }
}
