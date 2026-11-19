# Project Manager workspace

The manager workspace uses the existing project, milestone, document, audit and Identity storage. The AddDocumentUploaderIdentity migration adds a nullable, stable uploader user ID to documents. Apply it before starting the updated API.

## Available workflows

- Overview: assigned/active/completed projects, overdue project and milestone counts, document counts, delivery dates, milestone progress and project shortcuts.
- My projects: name/code search, status filter and project details including budget and dates.
- Schedule: select an assigned project; add, edit, complete, reopen and remove milestones. Project master dates remain administered by the company admin.
- Documents: upload, list and download files on assigned projects. Managers can remove only their own uploads; company admins can remove any document in their company. Existing signature validation, 10 MB file limit and 100 MB company quota apply.
- Reports: assigned portfolio, milestone progress, delivery risks, assigned-team counts, budgets separated by currency and CSV export.
- My activity: audit entries recorded under the manager's current email. This is personal activity, not a complete project-wide activity feed. Earlier entries under a previous email remain in the admin audit history.
- Profile & security: current email and role, email change with current password, password change with confirmation, sign out this browser and sign out all sessions.

## Authorization

`WorkspaceController` in the API accepts CompanyAdmin and ProjectManager. Every project resource is scoped through `AccessibleProjects`: same company, plus a current assignment for a manager. Reports and CSV use the same scope. Assignment removal revokes future requests for that project's milestones and documents. The existing JWT validator also checks active status, live roles, company and security stamp.

Account settings operate on the authenticated user. Employee activation retains a separate CompanyAdmin authorization requirement. Managers cannot use the admin MVC workspace or manage company-wide accounts.

The MVC ManagerWorkspace controller reuses the existing validated forms and API handling, with manager-specific authorization, navigation and empty-state links. Mutations retain antiforgery protection. Shared views preserve the admin layout for admin requests.

## Verification and running

Build `tests/AssignmentChecks/AssignmentChecks.csproj` in the `AssignmentChecks` configuration and run its DLL from the repository root. The harness uses a unique disposable LocalDB database, actual JWT authentication, API endpoints and rendered MVC pages. Manager checks cover assigned/unassigned/foreign projects, CSV isolation, milestone and document actions, assignment revocation, profile changes, session revocation, anti-forgery and navigation.

Restart both API and Web after rebuilding, then sign in with a ProjectManager account. All manager HTTP clients honour `Api:BaseUrl` (default `https://localhost:59093/api`). Browser visual verification remains separate from the automated rendered-HTML checks.

## Document ownership

New uploads store the authenticated user ID; email is kept only as a display snapshot. Email changes do not transfer ownership. The API returns a computed CanDelete capability and independently enforces it during deletion. Ownership does not bypass project assignment or company checks.

Legacy documents have no verified uploader ID and remain admin-only for deletion. They are not backfilled by email because an address could have changed or been reused. Existing file content is preserved by the migration. Previously deleted file content cannot be recovered by this update.
