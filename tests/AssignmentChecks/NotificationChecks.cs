using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

static class NotificationChecks
{
    public static async Task RunAsync(IServiceProvider services, string url, int companyId, int projectId,
        string engineerId, string engineerToken, string colleagueToken, string foreignToken, Action<bool, string> check)
    {
        using var client = new HttpClient { BaseAddress = new Uri(url) };
        using (var anonymous = await client.GetAsync("/api/notifications"))
            check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "Bell inbox rejects anonymous requests");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineerToken);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var first = await client.GetAsync("/api/notifications/stream", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        using var second = await client.GetAsync("/api/notifications/stream", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        check(first.IsSuccessStatusCode && first.Content.Headers.ContentType?.MediaType == "text/event-stream", "Authenticated bell opens SSE stream");
        using var reader = new StreamReader(await first.Content.ReadAsStreamAsync(deadline.Token));
        using var otherReader = new StreamReader(await second.Content.ReadAsStreamAsync(deadline.Token));
        async Task<JsonElement> Snapshot(StreamReader source)
        {
            while (await source.ReadLineAsync(deadline.Token) is string line)
                if (line.StartsWith("data: ")) return JsonSerializer.Deserialize<JsonElement>(line[6..]);
            throw new Exception("Notification stream ended before snapshot");
        }
        await Snapshot(reader); await Snapshot(otherReader);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var notice = new WorkspaceNotification { CompanyId = companyId, ProjectId = projectId,
            RecipientUserId = engineerId, Message = "Live delivery integration check" };
        db.WorkspaceNotifications.Add(notice);
        await db.SaveChangesAsync();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var pushed = await Snapshot(reader);
        var pushedOther = await Snapshot(otherReader);
        check(pushed.GetProperty("items").EnumerateArray().Any(n => n.GetProperty("id").GetInt64() == notice.Id) &&
            pushedOther.GetProperty("items").EnumerateArray().Any(n => n.GetProperty("id").GetInt64() == notice.Id) && timer.Elapsed < TimeSpan.FromSeconds(5),
            "Saved notification pushes to both tabs without polling delay");
        using (var read = await client.PostAsync($"/api/notifications/{notice.Id}/read", null))
            check(read.IsSuccessStatusCode, "Bell marks own notification read");
        var readPush = await Snapshot(reader);
        check(readPush.GetProperty("items").EnumerateArray().Single(n => n.GetProperty("id").GetInt64() == notice.Id).GetProperty("isRead").GetBoolean(),
            "Read state is pushed to connected inbox");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", colleagueToken);
        var before = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        using (var wrong = await client.PostAsync($"/api/notifications/{notice.Id}/read", null))
            check(wrong.StatusCode == HttpStatusCode.NotFound, "Bell read rejects another recipient");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);
        var foreign = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        check(!foreign.GetProperty("items").EnumerateArray().Any(n => n.GetProperty("id").GetInt64() == notice.Id), "Bell inbox isolates companies");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineerToken);
        using (var all = await client.PostAsync("/api/notifications/read-all", null)) check(all.IsSuccessStatusCode, "Bell mark-all succeeds");
        var own = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        check(own.GetProperty("unread").GetInt32() == 0, "Mark-all clears full unread count");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", colleagueToken);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/notifications");
        check(before.GetProperty("unread").GetInt32() == after.GetProperty("unread").GetInt32(), "Mark-all preserves other recipients' unread counts");
        // Subscribe directly to verify explicit transactions signal only after commit.
        var signals = services.GetRequiredService<NotificationSignal>();
        using var subscription = signals.Subscribe(companyId, engineerId);
        using var signalDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = subscription.WaitAsync(signalDeadline.Token);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Entry(notice).ReloadAsync();
            notice.ReadAtUtc = null;
            await db.SaveChangesAsync();
            check(!waiting.IsCompleted, "Uncommitted notification does not publish");
            await transaction.CommitAsync();
        }
        await waiting;
        check(true, "Transaction commit publishes notification signal");
    }
}
