using System.Net.Http.Headers;
using ConstructionSaaS.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
namespace ConstructionSaaS.Web.Controllers;
public class NotificationsController(IHttpClientFactory factory,IConfiguration config):Controller
{
    [HttpGet]
    public async Task Stream(CancellationToken ct)
    {
        var token=HttpContext.Session.GetString("JwtToken");
        if(string.IsNullOrEmpty(token)){Response.StatusCode=401;return;}
        Response.ContentType="text/event-stream"; Response.Headers.CacheControl="no-store"; Response.Headers["X-Accel-Buffering"]="no";
        using var client=factory.CreateClient(); client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        try {
            using var response = await client.GetAsync(ApiEndpoint.Resolve(config) + "/notifications/stream", HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) {
                var name = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden ? "unauthorized" : "unavailable";
                await Response.WriteAsync($"event: {name}\ndata: {{}}\n\n", ct);
                await Response.Body.FlushAsync(ct); return;
            }
            HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
            using var upstream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[8192];
            int read;
            while ((read = await upstream.ReadAsync(buffer, ct)) > 0) {
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                await Response.Body.FlushAsync(ct);
            }
        } catch(OperationCanceledException) {} catch(HttpRequestException) {}
    }
    [HttpGet]
    public async Task<IActionResult> Inbox(CancellationToken ct) {
        var token=HttpContext.Session.GetString("JwtToken");if(string.IsNullOrEmpty(token))return Unauthorized();
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await client.GetAsync(ApiEndpoint.Resolve(config)+"/notifications",ct);
        Response.Headers.CacheControl="no-store";
        if(!response.IsSuccessStatusCode)return StatusCode((int)response.StatusCode);
        return Content(await response.Content.ReadAsStringAsync(ct),"application/json");
    }
    [HttpPost,ValidateAntiForgeryToken]
    public Task<IActionResult> ReadAll(CancellationToken ct) => Update("/read-all",ct);
    [HttpPost,ValidateAntiForgeryToken]
    public Task<IActionResult> Read(long id,CancellationToken ct) => Update($"/{id}/read",ct);
    private async Task<IActionResult> Update(string path,CancellationToken ct){
        var token=HttpContext.Session.GetString("JwtToken");if(string.IsNullOrEmpty(token))return Unauthorized();
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        using var response=await client.PostAsync(ApiEndpoint.Resolve(config)+"/notifications"+path,null,ct);
        return StatusCode((int)response.StatusCode);
    }
}
