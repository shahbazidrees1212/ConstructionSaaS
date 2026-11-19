using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using ConstructionSaaS.Application.DTOs.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ConstructionSaaS.Web.Infrastructure;

// Navigation guard for friendly redirects; API middleware independently enforces access.
public class SubscriptionAccessFilter(IHttpClientFactory factory,IConfiguration config):IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        var controller=context.RouteData.Values["controller"]?.ToString();
        var action=context.RouteData.Values["action"]?.ToString();
        var token=context.HttpContext.Session.GetString("JwtToken");
        var securityPage = controller is "Workspace" or "ManagerWorkspace" or "EngineerWorkspace" && action is "Settings" or "ChangeEmail" or "ChangePassword" or "SignOutAll";
        if(controller is "Notifications" or "Account" or "Home" or "Subscription" or "PasswordRecovery" || securityPage || (controller=="Billing"&&action is "Pay" or "Begin" or "Confirm") || string.IsNullOrEmpty(token)) {await next();return;}
        try {
            var jwt=new JwtSecurityTokenHandler().ReadJwtToken(token);
            if(jwt.Claims.Any(c=>(c.Type=="role"||c.Type==System.Security.Claims.ClaimTypes.Role)&&c.Value=="PlatformAdmin")) {context.Result=new RedirectToActionResult("Platform","Subscription",null);return;}
            using var client=factory.CreateClient();
            using var request=new HttpRequestMessage(HttpMethod.Get,ApiEndpoint.Resolve(config)+"/subscriptions");
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using var response=await client.SendAsync(request);
            if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized){context.Result=new RedirectToActionResult("Login","Account",null);return;}
            if(!response.IsSuccessStatusCode){context.Result=new StatusCodeResult(503);return;}
            var state=await response.Content.ReadFromJsonAsync<SubscriptionOverviewDto>();
            if(state==null){context.Result=new StatusCodeResult(503);return;}
            if(!state.HasAccess){context.Result=new RedirectToActionResult("Index","Subscription",null);return;}
        } catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or ArgumentException) {context.Result=new StatusCodeResult(503);return;}
        await next();
    }
}
