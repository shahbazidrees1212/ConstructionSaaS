BEGIN TRANSACTION;
GO

ALTER TABLE [ProjectDocuments] ADD [UploadedByUserId] nvarchar(450) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261106074010_AddDocumentUploaderIdentity', N'8.0.0');
GO

COMMIT;
GO

