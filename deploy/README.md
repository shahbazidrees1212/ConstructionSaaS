# Deployment runbook

This app targets **.NET 10**, tested with SDK 10.0.403 / runtime 10.0.14. Install a compatible Visual Studio version and .NET 10 SDK for IDE builds. The workspace-local SDK is available at `.tools/dotnet10/dotnet.exe`; `scripts/Publish.ps1` uses it when present. The SDK installer is not a substitute for a production ASP.NET Core hosting bundle.

The payment model is company-to-platform SaaS subscriptions through Stripe or PayPal. Follow [SUBSCRIPTIONS.md](SUBSCRIPTIONS.md) for owner credentials, recurring plans, webhooks and access enforcement. Pricing and production activation require owner configuration. These instructions are not evidence of a live deployment or a verified live recurring charge.

## Build and release

1. Restore from NuGet, including vulnerability checks: `dotnet restore ConstructionSaaS.sln`.
2. Build and run `tests/AssignmentChecks` on Windows with SQL LocalDB. The harness creates and removes only a uniquely named test database. Never point it at production.
3. Run `./scripts/Publish.ps1` to publish API and Web independently under `artifacts/publish`.
4. Back up the production database and verify a restore into a separate database before release.
5. Generate a reviewed SQL migration script using matching EF 10 tooling; apply with a deployment identity. Do not give the application identity schema-owner permissions. No automatic runtime schema migration is configured. Run `deploy/seed-roles.sql` once after migrations on a new database. Automatic role seeding is enabled only in Development unless explicitly overridden.
6. Configure the values below via protected deployment settings/environment variables. Do not commit credentials or Data Protection keys.
7. Start API, then Web. Both `/health/live` endpoints indicate process liveness. `/health/ready` checks API/database connectivity and pending EF migrations. Readiness deliberately returns generic health output, not credentials/database details.
8. Smoke-test login/logout and all three roles, a project/task lifecycle, evidence download, password reset email, tenant isolation, and a restarted application. Confirm that failed readiness takes the instance out of traffic.

## API settings

| Environment variable | Required value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AllowedHosts` | Exact API hostname, e.g. `api.example.com` |
| `ConnectionStrings__DefaultConnection` | Production SQL Server connection with encryption, valid server certificate and least-privilege credentials |
| `Jwt__Key` | Independently generated random signing secret, minimum 32 bytes; never use a sample key |
| `Jwt__Issuer` / `Jwt__Audience` | Stable deployment-specific values |
| `DataProtection__KeyPath` | Absolute durable directory outside the publish folder, writable only by the app identity |
| `DataProtection__CertificateThumbprint` | Optional on Windows (DPAPI default); required for configured non-Windows persistent keys, installed with accessible private key |
| `PasswordRecovery__WebBaseUrl` | Trusted public HTTPS Web origin |
| `Smtp__Host`, `Smtp__Port`, `Smtp__From`, `Smtp__Username`, `Smtp__Password` | Real SMTP submission credentials; default port 587 with STARTTLS |

## Web settings

| Environment variable | Required value |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `AllowedHosts` | Exact Web hostname |
| `Api__BaseUrl` | HTTPS API base ending in `/api`, e.g. `https://api.example.com/api` |
| `DataProtection__KeyPath` | Separate durable Web key directory |
| `DataProtection__CertificateThumbprint` | Same protection requirements as API |

All Web controllers now use `Api:BaseUrl`; there are no hard-coded development API calls. Local development uses `appsettings.Development.json`. API TLS certificate verification remains enabled.

## Windows/IIS baseline

For an IIS deployment, install the current **.NET 10 Hosting Bundle**, use separate API/Web application pools with dedicated least-privilege identities, enable HTTPS bindings and deploy each publish folder to its own site. Grant each identity access to its own key directory. Windows DPAPI ties keys to the service identity: retain that identity/profile and back up keys securely. Configure `Load User Profile` for the pools when using per-user DPAPI. For multiple machines, use a shared protected key store and encryption certificate instead.

The current Web session cache is **single-instance in memory**. A restart signs users out. Before scaling Web to multiple replicas, configure a shared session cache and shared protected key ring; sticky routing alone does not preserve sessions on failover. Do not claim multi-instance readiness with the in-memory cache.

Behind an external reverse proxy, configure only its explicit IP in `ReverseProxy__KnownProxies__0`. The app trusts one forwarding hop. Never clear proxy allowlists or trust arbitrary forwarded headers. IIS integration handles its local forwarding. Terminate TLS at a trusted proxy and firewall private application ports.

## Operations and release gates

- Backups: SQL database includes documents and evidence. Schedule encrypted backups, retain off-host copies, and exercise recovery with agreed RPO/RTO. Protect and separately back up key stores.
- Monitoring: collect structured server logs without tokens/passwords; alert on readiness failures, unexpected 5xx rates, storage pressure and backup failures. Error pages expose only a reference ID.
- Uploads: current extension/signature checks are not malware scanning. Configure malware scanning/quarantine if accepting untrusted public uploads before public launch.
- Dependency audit: run `dotnet list package --vulnerable --include-transitive` with network access for every release.
- Email: verify delivery with the real SMTP provider, SPF/DKIM and the public reset-link origin.
- Billing: Stripe/PayPal project invoicing is implemented. Follow [PAYMENTS.md](PAYMENTS.md) for the migration, per-company merchant secrets, webhook setup, limitations and real sandbox acceptance tests. No merchant credentials are included, and live payment operation has not been verified.
- Browser evidence under `artifacts/ui` is test data, not production customer screenshots.

Microsoft references: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer), [Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview).
