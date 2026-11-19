using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Application.DTOs.Workspace;
using ConstructionSaaS.Web.Infrastructure;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConstructionSaaS.Web.Controllers;
public class BillingController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var isPublic = context.ActionDescriptor.RouteValues["action"] is nameof(Pay) or nameof(Begin) or nameof(Confirm);
        if (!isPublic) {
            var token = HttpContext.Session.GetString("JwtToken");
            var reader = new JwtSecurityTokenHandler();
            try {
                if (string.IsNullOrEmpty(token) || !reader.CanReadToken(token) || reader.ReadJwtToken(token).ValidTo <= DateTime.UtcNow) { context.Result = RedirectToAction("Login", "Account"); return; }
                if (!reader.ReadJwtToken(token).Claims.Any(c => (c.Type == "role" || c.Type == ClaimTypes.Role) && c.Value == "CompanyAdmin")) { context.Result = StatusCode(403); return; }
            } catch (ArgumentException) { context.Result = RedirectToAction("Login", "Account"); return; }
        }
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (isPublic) Response.Headers["Content-Security-Policy"] = "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self' https://checkout.stripe.com https://www.paypal.com https://www.sandbox.paypal.com";
        var result = await next();
        if (result.Exception is BillingApiException api) {
            result.ExceptionHandled = true;
            if (api.Status == HttpStatusCode.NotFound) result.Result = NotFound();
            else if (api.Status == HttpStatusCode.Unauthorized && !isPublic) result.Result = RedirectToAction("Login", "Account");
            else if (api.Status == HttpStatusCode.Forbidden) result.Result = StatusCode(403);
            else if (!HttpMethods.IsPost(Request.Method)) result.Result = View("~/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
            else { TempData["ErrorMessage"] = api.Message; result.Result = isPublic ? RedirectToAction(nameof(Pay), new { link = Request.HasFormContentType ? Request.Form["link"].ToString() : "" }) : RedirectToAction(nameof(Index)); }
        } else if (result.Exception is HttpRequestException or TaskCanceledException or JsonException) {
            result.ExceptionHandled = true;
            if (isPublic && HttpMethods.IsPost(Request.Method)) {
                TempData["ErrorMessage"] = "Payment status could not be confirmed because the service did not respond. Do not submit a second payment. Return later and select Confirm payment, or contact the company.";
                result.Result = RedirectToAction(nameof(Pay), new { link = Request.HasFormContentType ? Request.Form["link"].ToString() : "" });
            } else result.Result = View("~/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
        }
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool anonymous = false)
    {
        using var client = factory.CreateClient("billing");
        if (!anonymous) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", HttpContext.Session.GetString("JwtToken"));
        using var request = new HttpRequestMessage(method, ApiEndpoint.Resolve(config) + path);
        if (body != null) request.Content = JsonContent.Create(body);
        var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        var message = "Could not complete the request. Please check the details and try again.";
        try {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (error.TryGetProperty("message", out var text)) message = text.GetString() ?? message;
            else if (error.TryGetProperty("errors", out var errors)) message = string.Join(" ", errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray()).Select(e => e.GetString()));
        } catch (JsonException) { }
        response.Dispose(); throw new BillingApiException(status, message);
    }
    private async Task<T> Get<T>(string path, bool anonymous = false)
    {
        using var response = await Send(HttpMethod.Get, path, anonymous: anonymous);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    public async Task<IActionResult> Index(int page = 1)
    {
        if (page < 1 || page > 100000) return BadRequest();
        return View(new BillingViewModel { Page = page, Invoices = await Get<List<InvoiceDto>>("/billing/invoices?page=" + page), Providers = await Get<List<PaymentProviderDto>>("/billing/providers") });
    }
    private async Task<BillingViewModel> Form(InvoiceInput input) => new() { Input = input, Projects = await Get<WorkspaceReportDto>("/workspace/reports"), Providers = await Get<List<PaymentProviderDto>>("/billing/providers") };
    [HttpGet] public async Task<IActionResult> Create() => View(await Form(new InvoiceInput()));
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] InvoiceInput input)
    {
        if (!ModelState.IsValid) return View(await Form(input));
        try {
            using var response = await Send(HttpMethod.Post, "/billing/invoices", input);
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            TempData["SuccessMessage"] = "Invoice issued. Share its payment link with your client.";
            return RedirectToAction(nameof(Details), new { id = result.GetProperty("id").GetGuid() });
        } catch (BillingApiException e) when (e.Status is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable) {
            ModelState.AddModelError("", e.Message); return View(await Form(input));
        }
    }
    [HttpGet] public async Task<IActionResult> Details(Guid id) => View(await Get<InvoiceDto>("/billing/invoices/" + id));
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Refresh(Guid id)
    { using var response = await Send(HttpMethod.Post, $"/billing/invoices/{id}/refresh"); TempData["SuccessMessage"] = "Payment status checked with the provider."; return RedirectToAction(nameof(Details), new { id }); }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Void(Guid id)
    { using var response = await Send(HttpMethod.Post, $"/billing/invoices/{id}/void"); TempData["SuccessMessage"] = "Invoice voided; its record has been retained."; return RedirectToAction(nameof(Details), new { id }); }
    [HttpGet] public async Task<IActionResult> Pay(string link)
    {
        if (string.IsNullOrEmpty(link) || link.Length > 100) return NotFound();
        return View(new InvoicePaymentViewModel { Link = link, Invoice = await Get<InvoiceDto>("/payments/invoice?link=" + Uri.EscapeDataString(link), true) });
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Begin(string link)
    {
        using var response = await Send(HttpMethod.Post, "/payments/checkout", new { link }, true);
        var result = (await response.Content.ReadFromJsonAsync<PaymentLinkDto>())!;
        if (!Uri.TryCreate(result.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.Host is not ("checkout.stripe.com" or "www.paypal.com" or "www.sandbox.paypal.com")) return BadRequest();
        return Redirect(result.Url);
    }
    [HttpPost, ValidateAntiForgeryToken] public async Task<IActionResult> Confirm(string link)
    {
        using var response = await Send(HttpMethod.Post, "/payments/confirm", new { link }, true);
        var invoice = (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
        TempData["SuccessMessage"] = invoice.Status == "Paid" ? "Payment confirmed. Thank you." : "Status checked. If payment is still processing, check again shortly.";
        return RedirectToAction(nameof(Pay), new { link });
    }
    private sealed class BillingApiException(HttpStatusCode status, string message) : Exception(message) { public HttpStatusCode Status { get; } = status; }
}
