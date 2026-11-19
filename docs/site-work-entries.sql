SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [SiteWorkEntries] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [TaskId] int NULL,
    [AuthorUserId] nvarchar(450) NOT NULL,
    [AuthorEmail] nvarchar(256) NOT NULL,
    [Kind] nvarchar(30) NOT NULL,
    [Summary] nvarchar(3000) NOT NULL,
    [Blockers] nvarchar(2000) NULL,
    [ReportDate] datetime2 NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [FileName] nvarchar(200) NULL,
    [ContentType] nvarchar(100) NULL,
    [Content] varbinary(max) NULL,
    CONSTRAINT [PK_SiteWorkEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SiteWorkEntries_ProjectTasks_TaskId] FOREIGN KEY ([TaskId]) REFERENCES [ProjectTasks] ([Id]),
    CONSTRAINT [FK_SiteWorkEntries_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
);
GO

CREATE UNIQUE INDEX [IX_SiteWorkEntries_ProjectId_AuthorUserId_ReportDate] ON [SiteWorkEntries] ([ProjectId], [AuthorUserId], [ReportDate]) WHERE [TaskId] IS NULL AND [ReportDate] IS NOT NULL;
GO

CREATE INDEX [IX_SiteWorkEntries_TaskId] ON [SiteWorkEntries] ([TaskId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261109110640_AddSiteWorkEntries', N'8.0.0');
GO

COMMIT;
GO

