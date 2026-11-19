# Project audit — 19 November 2026

## Assessment

ConstructionSaaS has a credible foundation for a supervised pilot: separate company/manager/engineer workflows, project assignment boundaries, task submission and approval, daily reports, evidence, notifications and billing. The main dashboards share a usable visual language, although some admin forms retain the older blue/orange treatment. It should not yet be presented as a fully proven enterprise construction platform.

This is a source review and automated integration/browser audit of the local workspace, not a penetration test, production deployment certification, load test or field-user study. Existing uncommitted work was preserved. Tests use disposable databases and fixture accounts, not customer data.

## Changes made in this audit

| Finding | Change |
| --- | --- |
| Deactivated companies could retain access through existing user JWTs; login checked user activity but not company activity. | Login and JWT validation now require an active company. An open notification stream also checks the company before each snapshot. Company-less platform accounts retain their existing behavior. |
| Login responses disclosed locked/deactivated account states. | Invalid, locked and inactive accounts receive the same credential failure response. Existing Identity lockout remains in place and is tested. This fixes message disclosure, not all possible timing-based enumeration. |
| Login HTTP 5xx looked like a wrong password; API timeouts could escape the normal form error path. | Login distinguishes temporary service failure and throttling without revealing account state. Login and registration show a retry message on service timeout; their clients/responses are disposed. |
| Mobile users scrolled through a full task tutorial before reaching daily work. | Workflow instructions now use a keyboard-accessible, collapsed native disclosure. Instructions remain available on demand. |
| Overdue badge replaced the actual task state; internal labels such as PendingReview appeared on cards. | Cards preserve readable task state and show overdue separately. |
| Creating an unassigned task incorrectly claimed an engineer had been notified. | Success feedback now asks the manager to assign an engineer when ready. |
| Task removal confirmation did not mention evidence/work-note loss. | Confirmation explicitly names those records; existing deletion behavior is unchanged. |
| The dedicated payment-provider test command was absent from CI. | Added a CI step for provider-response and signature checks. Local execution is verified separately; a remote CI run is not claimed. |

Company stream revocation is checked on the next snapshot (normally within the existing 20-second reconciliation interval without a new signal). It is not an immediate cross-process revocation broadcast. These changes require rebuilding and restarting API and Web; no production deployment or database migration was performed.

## Reviewed areas

| Area | Evidence and scope |
| --- | --- |
| Tenant and role access | Company/project scoping, assignment eligibility, foreign-company rejection, manager/engineer visibility; HTTP regression suite. |
| Authentication | Identity login/lockout, active users and companies, JWT claims/security stamps, password recovery and sign-out-all. |
| Tasks and site work | Create/edit/assignment, status concurrency, manager approval/reopening, work notes, file evidence, duplicate daily-report prevention. |
| Notifications | Recipient isolation, actor exclusion, actual destination route, read state, two-tab SSE delivery, transaction commit signals, reconnect. |
| Documents and reports | File type/signature/size checks, tenant/owner downloads, CSV formula escaping, reports and activity pagination. |
| Money workflows | Invoice totals/provider binding, hosted checkout URL validation, idempotency, provider failures, subscription access/cancellation/renewal; test providers only. |
| UI | Admin, manager and engineer routes at 360, 390, 768 and 1440 CSS pixels; response/overflow, navigation, forms, bell interactions and representative screenshot inspection. |
| Deployment/dependencies | Production headers, trusted forwarding, Data Protection, session storage, health checks, deployment runbook, CI, NuGet vulnerability audit. |

## Remaining engineering work

These are concrete limitations found in the current design, not features implemented by this audit.

1. **Public launch: abuse protection and account security.** Account lockout exists, but login/registration have no endpoint-level rate policy. Add tuned controls at the public Web/API boundary, considering that Web-to-API traffic shares an IP. Avoid blindly limiting all customers under the Web server's address. Add verified email/invitations and MFA for administrators. Generic response wording alone does not eliminate timing-based enumeration. [OWASP authentication guidance](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html), [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).
2. **Record integrity.** Task status is a concurrency token, but task detail edits do not carry a full row version from the form. Two managers editing details under the same status can overwrite changes. Introduce full optimistic concurrency through DTOs/forms and test stale edits. Prefer archive/restore and an explicit retention policy for projects, tasks and evidence over routine permanent deletion.
3. **Stable audit identity.** Some history visibility uses actor email. Changing email can hide earlier personal history. Add immutable actor user IDs while preserving display email; plan migration/backfill without guessing ownership for historical email collisions.
4. **Growth and performance.** Task overview materializes all accessible tasks and several related datasets; team retrieval asks for roles per user. Add server pagination/filtering and query projections, then load-test realistic company sizes. Work entries are limited to the latest 200 and task history to 100, so make retrieval of older records explicit. Do not silently truncate operational exports.
5. **Uploads and storage.** Signature validation is not malware scanning. Current documents/evidence live in SQL and have fixed quotas. Add quarantine/scanning, authorized object-storage downloads and configurable quotas before accepting high-volume external uploads.
6. **Multiple instances.** Web sessions are in-memory and immediate notification signals are process-local. Scaling needs shared session storage/protected keys and a durable outbox/pub-sub strategy. The existing 20-second reconciliation provides eventual notification updates across instances, not instant delivery.
7. **Operations.** Exercise backup restore, monitoring/alerting, real SMTP delivery and provider sandbox acceptance using owner credentials. Existing tests do not prove production email, merchant onboarding, live charges, refund operations or recovery objectives. Browser checks are currently local; add a supported Windows browser job to CI.
8. **Cancellation logging.** During browser navigation, a cancelled SQL notification query produced a transient SQL 3980 log in the test host. Evaluate narrowly handling confirmed request cancellation and ensure reconnect under load; do not suppress unrelated database exceptions. Production enables SQL retries, while the test host does not.

## UI and market fit

**What works:** green/cream main dashboards, distinct role navigation, clear primary actions, visible empty/error states, project filters, task approval language and a compact notification inbox. The engineer layout supports narrow screens, and the collapsible workflow guide reduces repeated scrolling.

**What still needs product validation:** the manager sidebar is long, and dashboard hero sections occupy space that frequent users may prefer for urgent work. Prioritize “Needs my approval”, overdue/blockers and today's work above decorative content. Group navigation by Projects, Field work and Administration; unify legacy blue/orange creation forms with the green/cream dashboard through shared theme tokens. Validate contrast, touch targets, zoom, screen-reader reading order and outdoor phone readability with real users. Responsive screenshots alone do not establish WCAG compliance or field usability. Time/date conventions currently mix explicit UTC business dates and local bell timestamps; add company timezone/locale settings with deliberate date-only semantics.

Recommended next features, in order:

| Priority | Feature | Concrete useful outcome |
| --- | --- | --- |
| 1 | RFI and submittal approvals | A project question/submission has an owner, due date, revision, comments and a recorded approval; overdue work reaches the responsible person. |
| 2 | Drawing/document revisions | Teams can identify the current approved drawing, see superseded versions, and link a task or issue to the exact revision. |
| 3 | Offline daily reports and evidence drafts | An engineer can prepare a report/photo draft without connectivity and see queued, synced or conflicted state on reconnect; prevent duplicate submission and cross-account draft exposure. |
| 4 | Punch list / snagging | Location, owner, severity, photos and verified closure support inspection and handover. |
| 5 | Change orders and budget variance | Approved scope changes have an amount, reason and audit trail; projected versus actual cost is separate from invoice/payment status. |
| 6 | Notification preferences and escalation | Users choose relevant channels; urgent overdue/review work escalates with deduplication and delivery history. |

This prioritization is a product recommendation inferred from the app's gaps and established construction workflows, not evidence that every customer needs every feature. Procore documents RFIs, submittals, daily logs and punch lists in its [project overview](https://support.procore.com/products/online/user-guide/project-level/project-overview/tutorials/about-the-project-overview). Autodesk documents explicit download/sync/error states for field work in its [mobile sync status guidance](https://help.autodesk.com/cloudhelp/ENU/Build-Mobile/files/download-project/Sync_Status.html). These support choosing construction-specific workflows over adding generic dashboard widgets.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build tests/AssignmentChecks/AssignmentChecks.csproj -c AssignmentChecks --no-restore -m:1 -v:minimal` | Passed; 0 warnings, 0 errors. |
| Main suite with `UI_CAPTURE=1` | **409 checks passed**, including new login/lockout/company-deactivation regressions and browser suite assertions. |
| Browser details | **172 page/viewport checks, 0 failures:** admin 84, engineer 32, manager 56; widths 360/390/768/1440. |
| `--subscription-only` | **39 checks passed.** |
| `--payment-provider-only` | **14 checks passed.** These provider checks also appear in the main suite; counts are not additive unique coverage. |
| `dotnet list ConstructionSaaS.sln package --vulnerable --include-transitive` | No known vulnerable packages reported for the five application projects using configured NuGet sources. |
| `git diff --check` | Passed; existing line-ending normalization warnings only. |

Detailed local logs are under `artifacts/project-audit-*.log`; browser screenshots/results are under `artifacts/ui/{admin,manager,engineer}`. Artifacts contain fixture data. The main log includes expected rejection scenarios and the cancellation issue recorded above; passing assertions do not mean the log is error-free. The hosted payment/SMTP services and real customer environment were not exercised.
