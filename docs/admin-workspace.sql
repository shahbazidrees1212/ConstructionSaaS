BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    CREATE TABLE [ProjectDocuments] (
        [Id] int NOT NULL IDENTITY,
        [ProjectId] int NOT NULL,
        [FileName] nvarchar(200) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [Content] varbinary(max) NOT NULL,
        [Size] bigint NOT NULL,
        [UploadedBy] nvarchar(256) NOT NULL,
        [UploadedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProjectDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjectDocuments_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    CREATE TABLE [ProjectMilestones] (
        [Id] int NOT NULL IDENTITY,
        [ProjectId] int NOT NULL,
        [Title] nvarchar(160) NOT NULL,
        [Notes] nvarchar(2000) NULL,
        [DueDateUtc] datetime2 NOT NULL,
        [Progress] int NOT NULL,
        [CompletedAtUtc] datetime2 NULL,
        [UpdatedAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_ProjectMilestones] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProjectMilestones_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    CREATE TABLE [WorkspaceActivities] (
        [Id] bigint NOT NULL IDENTITY,
        [CompanyId] int NOT NULL,
        [Category] nvarchar(50) NOT NULL,
        [Description] nvarchar(500) NOT NULL,
        [Actor] nvarchar(256) NOT NULL,
        [OccurredAtUtc] datetime2 NOT NULL,
        CONSTRAINT [PK_WorkspaceActivities] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    CREATE INDEX [IX_ProjectDocuments_ProjectId] ON [ProjectDocuments] ([ProjectId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    CREATE INDEX [IX_ProjectMilestones_ProjectId_DueDateUtc] ON [ProjectMilestones] ([ProjectId], [DueDateUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    CREATE INDEX [IX_WorkspaceActivities_CompanyId_OccurredAtUtc] ON [WorkspaceActivities] ([CompanyId], [OccurredAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261103123115_AddAdminWorkspace'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261103123115_AddAdminWorkspace', N'8.0.0');
END;
GO

COMMIT;
GO

