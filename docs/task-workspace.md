# Tasks, engineer dashboard and notifications

## Roles and workflow

CompanyAdmin assigns project membership using Project Assignments. A ProjectManager can create, edit, assign, reassign and remove tasks only within their currently assigned company projects. Eligible task recipients must be active SiteEngineers in the same company and already assigned to that project. Unassigned draft tasks are allowed.

SiteEngineer login leads to the task dashboard. Engineers see only tasks assigned to themselves within projects to which they still belong. They may update Pending, InProgress or OnHold status and submit PendingReview with a completion summary. Managers approve Completed or reopen with a reason; they cannot create, delete or reassign tasks. Updates include the expected previous status; stale updates return 409, and the EF concurrency token protects simultaneous status writes.

The manager board flags tasks whose assignee is no longer eligible, while keeping their email visible. Revoking project membership immediately removes project tasks, notifications and history from that user's workspace. Inactive accounts and changed roles are rejected by the existing JWT validator.

Work notes, evidence, daily reports and recovery setup are documented in [site-work-and-recovery.md](site-work-and-recovery.md). Submitted and completed tasks must be reopened before editing details or assignment.

## UI and reports

The TaskBoard page shares the professional manager/engineer layout and supports project/status/search filters, manager task forms and engineer status controls. Separate tabs provide notifications, task completion reports and project audit history. Manager overview shows overdue task counts alongside milestone alerts; project details link directly to its task board.

Task completion means completed task count divided by total tasks. Milestone progress remains a separate metric. Authenticated CSV exports obey the same user/project filters as the task board and escape spreadsheet formula prefixes. All MVC mutations use antiforgery tokens.

## In-app notifications and audit

Task changes notify the assigned engineer and project managers; milestone changes notify project members. These records are saved in the same SaveChanges transaction as the underlying change. Notification read state is recipient-specific. The inbox shows the latest 100 saved records, unread first. Live reminders cover incomplete tasks/milestones due within three days or overdue, calculated when the page loads. No email, push delivery, background scheduler or browser auto-refresh is included.

Task audit records capture actor email, action, status transition, priority, due date and assignee email. Managers see the latest 100 events for their assigned projects; engineers see their own events under their current email. Earlier audit records without a project reference remain available to admins. Project deletion removes tasks transactionally with other project data; audit history is retained.

## Database and verification

Apply AddTaskNotificationsAndProjectAudit after AddDocumentUploaderIdentity. It adds WorkspaceNotifications, a nullable ProjectId to WorkspaceActivities and lookup indexes. The reviewed SQL is in task-workspace.sql. Existing records are preserved.

The AssignmentChecks harness uses a unique disposable SQL LocalDB database and actual API JWT validation. TaskWorkflowChecks and TaskWebChecks cover assignee validation, manager/engineer/tenant isolation, status conflicts, removal/reassignment, notifications/read ownership, alerts, audit, exports, MVC forms, CSRF and role-aware rendered pages. Browser visual testing is separate from rendered-HTML checks.

Restart API and Web after rebuilding and applying the migration. Existing pending tasks remain intact; new notification/audit events start with subsequent changes.
