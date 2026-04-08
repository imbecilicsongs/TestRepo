-- ============================================================
-- SQL Server Estate Inventory - Seed Data
-- ============================================================

USE SQLInventory;
GO

-- ============================================================
-- Environments
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.Environments WHERE Name = 'Production')
    INSERT INTO dbo.Environments (Name, ColorHex) VALUES ('Production', '#dc3545');

IF NOT EXISTS (SELECT 1 FROM dbo.Environments WHERE Name = 'Development')
    INSERT INTO dbo.Environments (Name, ColorHex) VALUES ('Development', '#198754');

IF NOT EXISTS (SELECT 1 FROM dbo.Environments WHERE Name = 'Test')
    INSERT INTO dbo.Environments (Name, ColorHex) VALUES ('Test', '#0dcaf0');

IF NOT EXISTS (SELECT 1 FROM dbo.Environments WHERE Name = 'Staging')
    INSERT INTO dbo.Environments (Name, ColorHex) VALUES ('Staging', '#fd7e14');

IF NOT EXISTS (SELECT 1 FROM dbo.Environments WHERE Name = 'DR')
    INSERT INTO dbo.Environments (Name, ColorHex) VALUES ('DR', '#6f42c1');
GO

-- ============================================================
-- Sample Tags
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM dbo.Tags WHERE Name = 'Critical')
    INSERT INTO dbo.Tags (Name, ColorHex) VALUES ('Critical', '#dc3545');

IF NOT EXISTS (SELECT 1 FROM dbo.Tags WHERE Name = 'ERP')
    INSERT INTO dbo.Tags (Name, ColorHex) VALUES ('ERP', '#0d6efd');

IF NOT EXISTS (SELECT 1 FROM dbo.Tags WHERE Name = 'Reporting')
    INSERT INTO dbo.Tags (Name, ColorHex) VALUES ('Reporting', '#6f42c1');

IF NOT EXISTS (SELECT 1 FROM dbo.Tags WHERE Name = 'Legacy')
    INSERT INTO dbo.Tags (Name, ColorHex) VALUES ('Legacy', '#adb5bd');
GO

-- ============================================================
-- Sample SQL Instances
-- ============================================================
DECLARE @ProdEnvId INT = (SELECT EnvironmentId FROM dbo.Environments WHERE Name = 'Production');
DECLARE @DevEnvId  INT = (SELECT EnvironmentId FROM dbo.Environments WHERE Name = 'Development');

IF NOT EXISTS (SELECT 1 FROM dbo.SqlInstances WHERE ServerName = 'SQL-PROD-01' AND InstanceName IS NULL)
BEGIN
    INSERT INTO dbo.SqlInstances
        (ServerName, InstanceName, Port, EnvironmentId, SqlVersion, SqlEdition, SqlBuild,
         HostOperatingSystem, IsClustered, IsAlwaysOnEnabled, MaxMemoryMb, CpuCount,
         ServiceAccount, Notes, IsActive)
    VALUES
        ('SQL-PROD-01', NULL, 1433, @ProdEnvId, '2022', 'Enterprise',
         '16.0.1000.6', 'Windows Server 2022', 0, 1, 65536, 16,
         'DOMAIN\svc_sql_prod', 'Primary production SQL Server node. Part of AG-PROD-01.', 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.SqlInstances WHERE ServerName = 'SQL-PROD-02' AND InstanceName IS NULL)
BEGIN
    INSERT INTO dbo.SqlInstances
        (ServerName, InstanceName, Port, EnvironmentId, SqlVersion, SqlEdition, SqlBuild,
         HostOperatingSystem, IsClustered, IsAlwaysOnEnabled, MaxMemoryMb, CpuCount,
         ServiceAccount, Notes, IsActive)
    VALUES
        ('SQL-PROD-02', NULL, 1433, @ProdEnvId, '2022', 'Enterprise',
         '16.0.1000.6', 'Windows Server 2022', 0, 1, 65536, 16,
         'DOMAIN\svc_sql_prod', 'Secondary production SQL Server node (AG secondary).', 1);
END

IF NOT EXISTS (SELECT 1 FROM dbo.SqlInstances WHERE ServerName = 'SQL-DEV-01' AND InstanceName IS NULL)
BEGIN
    INSERT INTO dbo.SqlInstances
        (ServerName, InstanceName, Port, EnvironmentId, SqlVersion, SqlEdition, SqlBuild,
         HostOperatingSystem, IsClustered, IsAlwaysOnEnabled, MaxMemoryMb, CpuCount,
         ServiceAccount, Notes, IsActive)
    VALUES
        ('SQL-DEV-01', NULL, 1433, @DevEnvId, '2019', 'Developer',
         '15.0.4355.3', 'Windows Server 2019', 0, 0, 8192, 4,
         'DOMAIN\svc_sql_dev', 'Shared development SQL Server.', 1);
END
GO

-- ============================================================
-- Sample Databases
-- ============================================================
DECLARE @Prod01Id INT = (SELECT InstanceId FROM dbo.SqlInstances WHERE ServerName = 'SQL-PROD-01' AND InstanceName IS NULL);
DECLARE @DevId    INT = (SELECT InstanceId FROM dbo.SqlInstances WHERE ServerName = 'SQL-DEV-01'  AND InstanceName IS NULL);

IF @Prod01Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SqlDatabases WHERE InstanceId = @Prod01Id AND DatabaseName = 'OrdersDB')
    INSERT INTO dbo.SqlDatabases (InstanceId, DatabaseName, SizeMb, CompatibilityLevel, RecoveryModel, StateDesc, IsReadOnly, [Owner], CollationName)
    VALUES (@Prod01Id, 'OrdersDB', 51200, 160, 'FULL', 'ONLINE', 0, 'sa', 'SQL_Latin1_General_CP1_CI_AS');

IF @Prod01Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SqlDatabases WHERE InstanceId = @Prod01Id AND DatabaseName = 'CustomersDB')
    INSERT INTO dbo.SqlDatabases (InstanceId, DatabaseName, SizeMb, CompatibilityLevel, RecoveryModel, StateDesc, IsReadOnly, [Owner], CollationName)
    VALUES (@Prod01Id, 'CustomersDB', 20480, 160, 'FULL', 'ONLINE', 0, 'sa', 'SQL_Latin1_General_CP1_CI_AS');

IF @DevId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.SqlDatabases WHERE InstanceId = @DevId AND DatabaseName = 'OrdersDB_Dev')
    INSERT INTO dbo.SqlDatabases (InstanceId, DatabaseName, SizeMb, CompatibilityLevel, RecoveryModel, StateDesc, IsReadOnly, [Owner], CollationName)
    VALUES (@DevId, 'OrdersDB_Dev', 1024, 150, 'SIMPLE', 'ONLINE', 0, 'sa', 'SQL_Latin1_General_CP1_CI_AS');
GO

-- ============================================================
-- Sample Availability Group
-- ============================================================
DECLARE @Prod01Id INT = (SELECT InstanceId FROM dbo.SqlInstances WHERE ServerName = 'SQL-PROD-01' AND InstanceName IS NULL);
DECLARE @Prod02Id INT = (SELECT InstanceId FROM dbo.SqlInstances WHERE ServerName = 'SQL-PROD-02' AND InstanceName IS NULL);

IF @Prod01Id IS NOT NULL AND @Prod02Id IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.AvailabilityGroups WHERE AgName = 'AG-PROD-01')
BEGIN
    INSERT INTO dbo.AvailabilityGroups (PrimaryInstanceId, AgName, ClusterType, AutomatedBackupPreference, Notes)
    VALUES (@Prod01Id, 'AG-PROD-01', 'WSFC', 'SECONDARY', 'Primary production availability group');

    DECLARE @AgId INT = SCOPE_IDENTITY();

    INSERT INTO dbo.AgReplicas (AgId, InstanceId, Role, AvailabilityMode, FailoverMode, SeedingMode)
    VALUES (@AgId, @Prod01Id, 'PRIMARY',   'SYNCHRONOUS_COMMIT',  'AUTOMATIC', 'AUTOMATIC');

    INSERT INTO dbo.AgReplicas (AgId, InstanceId, Role, AvailabilityMode, FailoverMode, SeedingMode)
    VALUES (@AgId, @Prod02Id, 'SECONDARY', 'SYNCHRONOUS_COMMIT',  'AUTOMATIC', 'AUTOMATIC');
END
GO

PRINT 'Seed data inserted successfully.';
GO
