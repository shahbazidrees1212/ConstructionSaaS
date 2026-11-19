# Admin workspace operations

Features: project/team/company management, assignments, project milestones with progress and overdue dates, project document upload/download/removal, persistent activity history, currency-separated portfolio reports with CSV export, account email/password changes and sign-out-all.

- Milestone dates are calendar dates in UTC. Progress is the equally weighted average of entered milestone percentages. Projects with no milestones show "Not tracked". Project status is managed independently through Project Edit. Completed/cancelled projects are not counted as overdue.
- Documents are stored transactionally in SQL Server with their project. Only company administrators can upload/download/delete them. Supported files: PDF, PNG, JPG, UTF-8 TXT and CSV; 10 MB/file, 100 MB/company. Binary signature/UTF-8 validation and attachment-only downloads are implemented. No malware scanning or document versioning is provided.
- Audit history starts when this update is installed; existing historical actions cannot be reconstructed. It records action descriptions and actor email, never password/token/file contents. Project deletion retains its activity history and deletes milestones/documents/assignments.
- Changing password/email or using Sign out all rotates the Identity security stamp. All prior JWTs are rejected. Previously issued tokens without a security stamp require a fresh login after this update.
- Changes require the AddAdminWorkspace migration. Do not use EnsureCreated against an existing application database. Back up existing databases before deployment. Tests use isolated, uniquely named databases.
- Set `Api:BaseUrl` in the Web app to override the workspace API address (default `https://localhost:59093/api`). Existing older controllers still use their established localhost API configuration.
- Restore the SQL database to recover stored documents; include it in regular backups. Email changes require the current password. Email verification/reset-via-email and MFA are outside the current roadmap.

Reviewed migration artifact: `docs/admin-workspace.sql` (idempotent upgrade from AddProjectAssignments to AddAdminWorkspace). It creates only the three workspace tables and indexes; it does not drop or alter existing tables.

After updating an existing installation, stop/rebuild/restart both API and Web, then sign in again over HTTPS. Existing JWTs without the new security-stamp claim are deliberately rejected.
