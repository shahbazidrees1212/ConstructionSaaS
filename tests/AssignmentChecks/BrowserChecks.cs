using System.Diagnostics;

static class BrowserChecks
{
    public static async Task RunAsync(string url, string role, int projectId, Action<bool,string> check, string? invoiceId = null, string? invoiceLink = null)
    {
        if (Environment.GetEnvironmentVariable("UI_CAPTURE") != "1") return;
        var selectedRole = Environment.GetEnvironmentVariable("UI_ROLE");
        if (!string.IsNullOrEmpty(selectedRole) && selectedRole != role) return;
        var start = new ProcessStartInfo("python") { UseShellExecute = false };
        foreach (var argument in new[] { "tests/ui/check_responsive.py", "--url", url, "--role", role, "--project", projectId.ToString() }) start.ArgumentList.Add(argument);
        if (invoiceId != null) { start.ArgumentList.Add("--invoice-id"); start.ArgumentList.Add(invoiceId); }
        if (invoiceLink != null) { start.ArgumentList.Add("--invoice-link"); start.ArgumentList.Add(invoiceLink); }
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        check(process.ExitCode == 0, $"Browser responsive checks: {role} (360/390/768/1440 pixels)");
    }
}
