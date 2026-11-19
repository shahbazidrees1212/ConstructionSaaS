# Live notification inbox

The Admin, Project Manager and Site Engineer workspaces share the notification bell. It displays the latest 20 accessible updates, prioritizes unread items, links to the relevant project, and supports individual and bulk read actions. The total unread badge includes items beyond the visible 20.

## Delivery

The API provides `/api/notifications/stream` using server-sent events. The MVC app proxies the stream with its server-side session token; JWTs are not exposed in browser URLs. Each save signals the affected recipients after a successful save, or after the explicit database transaction commits. Bounded per-connection channels coalesce bursts and fan updates out to every connected tab. Reconnection retrieves a fresh persisted snapshot, so missed transport events do not lose notifications.

A 20-second reconciliation snapshot covers database changes made by other processes, external writers and project-access changes. Immediate delivery applies within one API process. Multiple API replicas require a shared pub/sub backplane for immediate cross-replica delivery; otherwise reconciliation supplies those updates. This implementation uses SSE, not SignalR.

Streams rotate after 60 seconds (or sooner when the JWT expires). Every snapshot checks the account, company, security stamp and roles and applies current project assignment authorization. Reconnection also runs the normal authentication/subscription middleware. Read actions retain antiforgery checks in MVC and recipient/project checks in the API.

The browser pauses its stream while the tab is hidden, reconnects when visible, preserves keyboard focus where possible, and provides connection/error feedback. Escape and outside clicks close the inbox. Existing stored notifications are the source of truth; no database migration is required.

## Verification

`dotnet build tests/AssignmentChecks/AssignmentChecks.csproj -c AssignmentChecks --no-restore -m:1`

`dotnet run --project tests/AssignmentChecks/AssignmentChecks.csproj -c AssignmentChecks --no-build --no-restore`

Set `UI_CAPTURE=1` to include Edge/Playwright checks at 360, 390, 768 and 1440 pixels. Screenshots and reports are written to `artifacts/ui`. The test runner uses a uniquely named disposable LocalDB database, not application data.

## Verified results

- Build: zero warnings and zero errors.
- Final integration run: 371 checks passed, including immediate delivery to two SSE connections, read synchronization, recipient/tenant isolation, assignment revocation, antiforgery and explicit transaction commit behavior.
- Browser checks across the verification runs: Admin 84, Site Engineer 32, Project Manager 56 page/viewport combinations; all passed. The final run repeated the Manager browser checks after correcting the Activity category filters on narrow screens.
- Browser interaction checks cover the live connection, inbox viewport boundaries, bulk read behavior and Escape dismissal, including after the bulk-read button becomes disabled.
- Screenshots inspected: Admin dashboard and mobile inbox, Engineer mobile task board and desktop inbox, Manager overview and corrected mobile Activity page.

## Recipient rule: exclude the actor

Automatic task, work-entry, milestone and admin project notifications exclude the authenticated user who performed the action, using the JWT user ID rather than the email/display name. Other eligible recipients continue to receive updates, and the actor remains in audit history. Background operations without an authenticated actor retain their normal recipients. Existing notifications are not deleted retroactively.
