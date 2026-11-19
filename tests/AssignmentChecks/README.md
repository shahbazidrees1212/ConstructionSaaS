# Admin workspace integration checks

Run on Windows with SQL Server LocalDB (MSSQLLocalDB):

```powershell
dotnet run --project tests/AssignmentChecks/AssignmentChecks.csproj -c AssignmentChecks
```

The runner returns nonzero on failure. It creates a uniquely named `ConstructionSaaS_AssignmentChecks_<guid>` database, applies the real migrations, and deletes only that database in cleanup. It never reads application connection strings. The separate build configuration avoids overwriting Debug DLLs used by a running application.

Verified coverage:
- Assignment creation/removal/reassignment, simultaneous duplicates and HTTP 409 mapping.
- Inactive employee details, role-change review rule and company-isolated assignments.
- AddAdminWorkspace migration preserves existing records; project deletion rolls back on database rejection and cascades correctly on success.
- Milestone create/edit/complete/reopen/remove, due dates and average project progress.
- Document type/signature/size validation, file-name sanitization, byte-exact attachment download, removal and tenant isolation.
- Persistent audit descriptions/actor emails, category filtering, pagination validation, reports and CSV export.
- Real Identity login and production JWT validation; email/password changes and sign-out-all reject previously issued tokens.
- Team reactivation and cross-company rejection.
- Web Schedule/Documents/Activity/Reports/Settings render via the compiled Razor views. Milestone forms enforce antiforgery, submit to the API and show the saved record after redirect. CSV download passes through the Web controller.

The Web test host supplies an authenticated session through test-only middleware; this header/middleware is not registered by the production app. Automated HTTP/Razor checks do not replace visual inspection in a browser at different viewport sizes. Document malware scanning, email-delivery verification/reset flows and MFA are not implemented.

Last verified run: 116 assertions passed. Build: 0 compiler errors/warnings; package audit reported NU1900 because the NuGet vulnerability feed was unreachable in the sandbox.

## Responsive browser verification

Install Python Playwright into `.tools/browser` (`python -m pip install --target .tools/browser playwright`) and use installed Microsoft Edge. Set `UI_CAPTURE=1` before running the integration executable. The harness checks real seeded admin/manager/engineer pages at 360, 390, 768 and 1440 pixels and stores screenshots and overflow reports in `artifacts/ui`. Fixture session headers only exist in this test host. Never enable fixture middleware in production.
