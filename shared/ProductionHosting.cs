using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ConstructionSaaS.Hosting;

public static class ProductionHosting
{
    public static void AddProductionHosting(this WebApplicationBuilder builder, string applicationName)
    {
        var config = builder.Configuration;
        var protection = builder.Services.AddDataProtection().SetApplicationName(applicationName);
        var keyPath = config["DataProtection:KeyPath"];
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        {
            var hosts = config["AllowedHosts"];
            if (string.IsNullOrWhiteSpace(hosts) || hosts.Split(';').Any(h => h.Trim() == "*"))
                throw new InvalidOperationException("Production requires an explicit AllowedHosts list.");
            if (string.IsNullOrWhiteSpace(keyPath) || !Path.IsPathFullyQualified(keyPath))
                throw new InvalidOperationException("Production requires an absolute DataProtection:KeyPath outside the publish directory.");
        }
        if (!string.IsNullOrWhiteSpace(keyPath))
        {
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
            var thumbprint = config["DataProtection:CertificateThumbprint"];
            if (!string.IsNullOrWhiteSpace(thumbprint))
            {
                using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly);
                var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: true)
                    .OfType<X509Certificate2>().SingleOrDefault(c => c.HasPrivateKey)
                    ?? throw new InvalidOperationException("Data Protection certificate/private key is unavailable.");
                protection.ProtectKeysWithCertificate(certificate);
            }
            else if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
            else if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Configure a Data Protection encryption certificate before production startup.");
        }
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var entry in config.GetSection("ReverseProxy:KnownProxies").GetChildren())
            {
                if (!IPAddress.TryParse(entry.Value, out var address))
                    throw new InvalidOperationException("ReverseProxy:KnownProxies must contain explicit IP addresses.");
                options.KnownProxies.Add(address);
            }
        });
        builder.Services.AddHealthChecks();
    }

    public static void UseProductionHeaders(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers["Content-Security-Policy"] = "object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers.CacheControl = "no-store";
            await next();
        });
        if (!app.Environment.IsDevelopment()) app.UseHsts();
    }
}
