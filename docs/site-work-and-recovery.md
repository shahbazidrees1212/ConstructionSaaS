# Site work and password recovery

## Task workflow

Manager creates a task and assigns an active SiteEngineer who belongs to the same company and project. New tasks are Pending. The engineer starts work (InProgress), records notes and optional PDF/PNG/JPG evidence, or reports OnHold with a required blocker reason. On completion, the engineer submits PendingReview with a required summary. Only a project manager can approve Completed or reopen the submission with a reason.

Submitted/completed tasks are locked for engineer status changes and additional evidence. Managers can add review notes. The task edit endpoint cannot bypass the status/review endpoint. Existing Completed tasks remain completed; there is no retroactive approval requirement.

Work updates are appended with author identity and UTC time. The board shows the latest 200 accessible work entries. Engineers access evidence only for their currently assigned tasks; managers access their currently assigned projects. Daily reports are unique per author/project/UTC date. Engineers see their own reports. Project/task deletion removes corresponding work entries and attachments; audit records remain.

Evidence allows PDF, PNG and JPEG with extension/signature validation, 10 MB per file and 100 MB per company evidence storage. Downloads require current project access and use attachment/no-store/nosniff headers. This evidence quota is separate from the existing document quota. File signature checks are not antivirus scanning.

## Database

Migration `20261109110640_AddSiteWorkEntries` adds the work-entry table and a filtered daily-report unique index. `site-work-entries.sql` is the reviewed delta from `AddTaskNotificationsAndProjectAudit`. Apply once to a database already at that migration, or use EF database update. No existing project/task records are rewritten.

## Password recovery configuration

The login screen links to `/PasswordRecovery/Forgot`. ASP.NET Identity generates a one-hour reset token; resetting changes the security stamp, revoking existing JWT sessions and preventing token reuse. Requests are limited to five per minute per API client IP. The MVC server proxies requests, so deployments may share that IP limit; configure a trusted-client-aware policy when deploying behind a proxy. Recovery responses do not disclose whether an account exists.

Set these in API user secrets or deployment environment variables, never committed credentials:

| Environment variable | Value |
| --- | --- |
| `PasswordRecovery__WebBaseUrl` | HTTPS Web app origin, e.g. `https://localhost:7192` |
| `Smtp__Host` | Your SMTP submission server |
| `Smtp__Port` | STARTTLS port, normally `587` |
| `Smtp__From` | Verified sender email |
| `Smtp__Username` | SMTP account |
| `Smtp__Password` | SMTP secret/app password |

Without SMTP and a trusted HTTPS WebBaseUrl, recovery returns a clear unavailable message. No reset tokens are returned by the API or written to application logs. Real SMTP delivery must be verified with your mail provider after configuration. Persist and protect ASP.NET Data Protection keys across deployment restarts/instances so outstanding links continue to work.

## Main files

- API `TaskWorkspaceController.cs` and `.Work.cs`: review transitions, work notes, daily reports and evidence authorization.
- Application `DTOs/ProjectTasks/WorkEntryDto.cs`, `TaskWorkspaceDto.cs`: work/status contracts.
- Domain `Entities/SiteWorkEntry.cs`; Infrastructure context, audit hook, migration and repositories: persistence, notifications and deletion cleanup.
- Web `TaskBoardController.Work.cs`, task board views: work forms, report history and role-specific review controls.
- API/Web `PasswordRecoveryController.cs`, API `Security/PasswordResetMail.cs`, recovery views: reset flow and mail delivery.
- `tests/AssignmentChecks`: database/API/MVC integration coverage.

## Validation on 9 November 2026

AssignmentChecks build succeeded. All 272 SQL LocalDB/API/MVC integration checks passed, including existing assignment and document regressions. The package vulnerability lookup emitted NU1900 because its configured network endpoint was unavailable; compilation had no errors. Real SMTP delivery and browser visual inspection are not covered by these checks.
