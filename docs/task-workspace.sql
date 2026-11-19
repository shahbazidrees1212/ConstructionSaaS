BEGIN TRANSACTION;
GO

ALTER TABLE [WorkspaceActivities] ADD [ProjectId] int NULL;
GO

CREATE TABLE [WorkspaceNotifications] (
    [Id] bigint NOT NULL IDENTITY,
    [CompanyId] int NOT NULL,
    [ProjectId] int NOT NULL,
    [RecipientUserId] nvarchar(450) NOT NULL,
    [Message] nvarchar(500) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ReadAtUtc] datetime2 NULL,
    CONSTRAINT [PK_WorkspaceNotifications] PRIMARY KEY ([Id])
);
GO

CREATE INDEX [IX_WorkspaceActivities_CompanyId_ProjectId_OccurredAtUtc] ON [WorkspaceActivities] ([CompanyId], [ProjectId], [OccurredAtUtc]);
GO

CREATE INDEX [IX_WorkspaceNotifications_CompanyId_RecipientUserId_CreatedAtUtc] ON [WorkspaceNotifications] ([CompanyId], [RecipientUserId], [CreatedAtUtc]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261107072059_AddTaskNotificationsAndProjectAudit', N'8.0.0');
GO

COMMIT;
GO

