-- Run with the deployment identity after EF migrations on a new database.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'COMPANYADMIN')
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(nvarchar(36), NEWID()), N'CompanyAdmin', N'COMPANYADMIN', CONVERT(nvarchar(36), NEWID()));
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'PROJECTMANAGER')
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(nvarchar(36), NEWID()), N'ProjectManager', N'PROJECTMANAGER', CONVERT(nvarchar(36), NEWID()));
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'SITEENGINEER')
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(nvarchar(36), NEWID()), N'SiteEngineer', N'SITEENGINEER', CONVERT(nvarchar(36), NEWID()));
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE NormalizedName = N'PLATFORMADMIN')
    INSERT dbo.AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
    VALUES (CONVERT(nvarchar(36), NEWID()), N'PlatformAdmin', N'PLATFORMADMIN', CONVERT(nvarchar(36), NEWID()));
COMMIT;
