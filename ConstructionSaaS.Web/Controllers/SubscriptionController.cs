using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using ConstructionSaaS.Application.DTOs.Billing;
using ConstructionSaaS.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConstructionSaaS.Web.Controllers;

public class SubscriptionController(IHttpClientFactory factory,IConfiguration config):Controller
{
    private bool Admin=>Roles().Contains("CompanyAdmin");
    private IEnumerable<string> Roles(){var token=HttpContext.Session.GetString("JwtToken");return string.IsNullOrEmpty(token)?[]:new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Where(c=>c.Type is "role" or "roles" || c.Type==ClaimTypes.Role).Select(c=>c.Value);}
    public override async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        var token=HttpContext.Session.GetString("JwtToken");
        try{if(string.IsNullOrEmpty(token)||new JwtSecurityTokenHandler().ReadJwtToken(token).ValidTo<=DateTime.UtcNow){context.Result=RedirectToAction("Login","Account");return;}}
        catch(ArgumentException){context.Result=RedirectToAction("Login","Account");return;}
        Response.Headers.CacheControl="no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
        Response.Headers["Content-Security-Policy"]="object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self' https://checkout.stripe.com https://billing.stripe.com https://www.paypal.com https://www.sandbox.paypal.com";
        var result=await next();
        if(result.Exception is HttpRequestException or TaskCanceledException or JsonException) {
            result.ExceptionHandled=true;
            result.Result=StatusCode(503,"Subscription service is unavailable. No payment has been confirmed. Please try checking status again later.");
        }
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method,string path,object? body=null)
    {
        using var client=factory.CreateClient("billing");
        using var request=new HttpRequestMessage(method,ApiEndpoint.Resolve(config)+"/subscriptions"+path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",HttpContext.Session.GetString("JwtToken"));
        if(body!=null)request.Content=JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    public async Task<IActionResult> Index()
    {
        using var response=await Send(HttpMethod.Get,"");
        if(response.StatusCode==HttpStatusCode.Unauthorized)return RedirectToAction("Login","Account");
        if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode,"Unable to load your company's subscription.");
        return View(await response.Content.ReadFromJsonAsync<SubscriptionOverviewDto>());
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(SubscribeInput input)
    {
        if(!Admin)return StatusCode(403);
        if(!ModelState.IsValid){TempData["ErrorMessage"]="Choose a valid plan and payment provider.";return RedirectToAction(nameof(Index));}
        using var response=await Send(HttpMethod.Post,"/checkout",input);
        if(!response.IsSuccessStatusCode)return await Failure(response);
        var checkout=await response.Content.ReadFromJsonAsync<PaymentLinkDto>();
        if(!Uri.TryCreate(checkout?.Url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo!=""||uri.Host is not("checkout.stripe.com" or "www.paypal.com" or "www.sandbox.paypal.com"))return BadRequest();
        return Redirect(checkout!.Url);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh()
    {
        if(!Admin)return StatusCode(403);
        using var response=await Send(HttpMethod.Post,"/refresh");
        if(!response.IsSuccessStatusCode)return await Failure(response);
        TempData["SuccessMessage"]="Subscription status checked with the payment provider.";return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel()
    {
        if(!Admin)return StatusCode(403);
        using var response=await Send(HttpMethod.Post,"/cancel");
        if(!response.IsSuccessStatusCode)return await Failure(response);
        TempData["SuccessMessage"]="Renewal cancellation confirmed. Your verified paid access remains available until the date shown.";return RedirectToAction(nameof(Index));
    }
    private async Task<IActionResult> Failure(HttpResponseMessage response)
    {
        var message="Subscription request failed. Check the current subscription before trying again.";
        try{var body=await response.Content.ReadFromJsonAsync<JsonElement>();if(body.TryGetProperty("message",out var text))message=text.GetString()??message;}catch(JsonException){}
        TempData["ErrorMessage"]=message;return RedirectToAction(nameof(Index));
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Portal()
    {
        if(!Admin)return StatusCode(403);
        using var response=await Send(HttpMethod.Post,"/portal");
        if(!response.IsSuccessStatusCode)return await Failure(response);
        var portal=await response.Content.ReadFromJsonAsync<PaymentLinkDto>();
        if(!Uri.TryCreate(portal?.Url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="billing.stripe.com"||!uri.IsDefaultPort||uri.UserInfo!="")return BadRequest();
        return Redirect(portal!.Url);
    }
    public async Task<IActionResult> Platform()
    {
        if(!Roles().Contains("PlatformAdmin"))return StatusCode(403);
        using var response=await Send(HttpMethod.Get,"/companies");
        if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode);
        return View(await response.Content.ReadFromJsonAsync<List<PlatformCompanyViewModel>>());
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ReconcileCompany(int id)
    {
        if(!Roles().Contains("PlatformAdmin"))return StatusCode(403);
        using var response=await Send(HttpMethod.Post,$"/companies/{id}/refresh");
        TempData[response.IsSuccessStatusCode?"SuccessMessage":"ErrorMessage"]=response.IsSuccessStatusCode?"Company subscription verified.":"Provider verification failed. Check server logs.";
        return RedirectToAction(nameof(Platform));
    }
}
public record PlatformCompanyViewModel(int Id,string Name,string Email,PlatformSubscriptionViewModel? Subscription);
public record PlatformSubscriptionViewModel(string PlanName,string Provider,string Status,bool IsSandbox,DateTime? PaidThroughUtc,bool CancelAtPeriodEnd,DateTime? LastVerifiedAtUtc);
