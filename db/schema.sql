-- ============================================================
-- SQL Server Estate Inventory Database Schema
-- ============================================================

USE master;
GO

-- Create database if it doesn't exist
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'SQLInventory')
BEGIN
    CREATE DATABASE SQLInventory;
END
GO

USE SQLInventory;
GO

-- ============================================================
-- Environments
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Environments')
BEGIN
    CREATE TABLE dbo.Environments (
        EnvironmentId INT IDENTITY(1,1) NOT NULL,
        Name          NVARCHAR(100)     NOT NULL,
        ColorHex      NVARCHAR(7)       NOT NULL CONSTRAINT DF_Environments_ColorHex DEFAULT '#6c757d',
        CONSTRAINT PK_Environments PRIMARY KEY (EnvironmentId),
        CONSTRAINT UQ_Environments_Name UNIQUE (Name)
    );
END
GO

-- ============================================================
-- Tags
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Tags')
BEGIN
    CREATE TABLE dbo.Tags (
        TagId    INT IDENTITY(1,1) NOT NULL,
        Name     NVARCHAR(100)     NOT NULL,
        ColorHex NVARCHAR(7)       NOT NULL CONSTRAINT DF_Tags_ColorHex DEFAULT '#0d6efd',
        CONSTRAINT PK_Tags PRIMARY KEY (TagId),
        CONSTRAINT UQ_Tags_Name UNIQUE (Name)
    );
END
GO

-- ============================================================
-- SqlInstances
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SqlInstances')
BEGIN
    CREATE TABLE dbo.SqlInstances (
        InstanceId            INT IDENTITY(1,1) NOT NULL,
        ServerName            NVARCHAR(255)     NOT NULL,
        InstanceName          NVARCHAR(128)     NULL,       -- NULL = default instance
        Port                  INT               NOT NULL CONSTRAINT DF_SqlInstances_Port DEFAULT 1433,
        EnvironmentId         INT               NOT NULL,
        SqlVersion            NVARCHAR(50)      NULL,       -- e.g. '2019', '2022'
        SqlEdition            NVARCHAR(100)     NULL,       -- e.g. 'Enterprise', 'Standard'
        SqlBuild              NVARCHAR(50)      NULL,       -- full build e.g. '15.0.4355.3'
        HostOperatingSystem   NVARCHAR(255)     NULL,
        IsClustered           BIT               NOT NULL CONSTRAINT DF_SqlInstances_IsClustered DEFAULT 0,
        IsAlwaysOnEnabled     BIT               NOT NULL CONSTRAINT DF_SqlInstances_IsAlwaysOnEnabled DEFAULT 0,
        MaxMemoryMb           INT               NULL,
        CpuCount              INT               NULL,
        ServiceAccount        NVARCHAR(255)     NULL,
        Notes                 NVARCHAR(MAX)     NULL,
        IsActive              BIT               NOT NULL CONSTRAINT DF_SqlInstances_IsActive DEFAULT 1,
        LastDiscoveredUtc     DATETIME2         NULL,
        CreatedUtc            DATETIME2         NOT NULL CONSTRAINT DF_SqlInstances_CreatedUtc DEFAULT SYSUTCDATETIME(),
        ModifiedUtc           DATETIME2         NOT NULL CONSTRAINT DF_SqlInstances_ModifiedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_SqlInstances PRIMARY KEY (InstanceId),
        CONSTRAINT FK_SqlInstances_Environments FOREIGN KEY (EnvironmentId) REFERENCES dbo.Environments (EnvironmentId),
        CONSTRAINT UQ_SqlInstances_ServerInstance UNIQUE (ServerName, InstanceName)
    );
END
GO

-- ============================================================
-- InstanceTags  (many-to-many)
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'InstanceTags')
BEGIN
    CREATE TABLE dbo.InstanceTags (
        InstanceId INT NOT NULL,
        TagId      INT NOT NULL,
        CONSTRAINT PK_InstanceTags PRIMARY KEY (InstanceId, TagId),
        CONSTRAINT FK_InstanceTags_Instances FOREIGN KEY (InstanceId) REFERENCES dbo.SqlInstances (InstanceId) ON DELETE CASCADE,
        CONSTRAINT FK_InstanceTags_Tags      FOREIGN KEY (TagId)      REFERENCES dbo.Tags (TagId)             ON DELETE CASCADE
    );
END
GO

-- ============================================================
-- SqlDatabases
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SqlDatabases')
BEGIN
    CREATE TABLE dbo.SqlDatabases (
        DatabaseId          INT IDENTITY(1,1) NOT NULL,
        InstanceId          INT               NOT NULL,
        DatabaseName        NVARCHAR(128)     NOT NULL,
        SizeMb              DECIMAL(18,2)     NULL,
        CompatibilityLevel  INT               NULL,         -- e.g. 150 = SQL 2019
        RecoveryModel       NVARCHAR(20)      NULL,         -- FULL, SIMPLE, BULK_LOGGED
        StateDesc           NVARCHAR(60)      NULL,         -- ONLINE, OFFLINE, etc.
        IsReadOnly          BIT               NOT NULL CONSTRAINT DF_SqlDatabases_IsReadOnly DEFAULT 0,
        [Owner]             NVARCHAR(128)     NULL,
        CollationName       NVARCHAR(128)     NULL,
        LastFullBackupUtc   DATETIME2         NULL,
        LastLogBackupUtc    DATETIME2         NULL,
        CreatedUtc          DATETIME2         NOT NULL CONSTRAINT DF_SqlDatabases_CreatedUtc DEFAULT SYSUTCDATETIME(),
        ModifiedUtc         DATETIME2         NOT NULL CONSTRAINT DF_SqlDatabases_ModifiedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_SqlDatabases PRIMARY KEY (DatabaseId),
        CONSTRAINT FK_SqlDatabases_Instances FOREIGN KEY (InstanceId) REFERENCES dbo.SqlInstances (InstanceId) ON DELETE CASCADE,
        CONSTRAINT UQ_SqlDatabases_InstanceDatabase UNIQUE (InstanceId, DatabaseName)
    );
END
GO

-- ============================================================
-- AvailabilityGroups
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AvailabilityGroups')
BEGIN
    CREATE TABLE dbo.AvailabilityGroups (
        AgId                       INT IDENTITY(1,1) NOT NULL,
        PrimaryInstanceId          INT               NOT NULL,
        AgName                     NVARCHAR(128)     NOT NULL,
        ClusterType                NVARCHAR(50)      NULL,   -- WSFC, NONE, EXTERNAL
        AutomatedBackupPreference  NVARCHAR(50)      NULL,
        Notes                      NVARCHAR(MAX)     NULL,
        CreatedUtc                 DATETIME2         NOT NULL CONSTRAINT DF_AvailabilityGroups_CreatedUtc DEFAULT SYSUTCDATETIME(),
        ModifiedUtc                DATETIME2         NOT NULL CONSTRAINT DF_AvailabilityGroups_ModifiedUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_AvailabilityGroups PRIMARY KEY (AgId),
        CONSTRAINT FK_AvailabilityGroups_PrimaryInstance FOREIGN KEY (PrimaryInstanceId) REFERENCES dbo.SqlInstances (InstanceId)
    );
END
GO

-- ============================================================
-- AgReplicas
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AgReplicas')
BEGIN
    CREATE TABLE dbo.AgReplicas (
        ReplicaId        INT IDENTITY(1,1) NOT NULL,
        AgId             INT               NOT NULL,
        InstanceId       INT               NOT NULL,
        Role             NVARCHAR(20)      NOT NULL,   -- PRIMARY, SECONDARY
        AvailabilityMode NVARCHAR(30)      NULL,       -- SYNCHRONOUS_COMMIT, ASYNCHRONOUS_COMMIT
        FailoverMode     NVARCHAR(20)      NULL,       -- AUTOMATIC, MANUAL
        SeedingMode      NVARCHAR(20)      NULL,       -- AUTOMATIC, MANUAL
        CONSTRAINT PK_AgReplicas PRIMARY KEY (ReplicaId),
        CONSTRAINT FK_AgReplicas_AG       FOREIGN KEY (AgId)       REFERENCES dbo.AvailabilityGroups (AgId)    ON DELETE CASCADE,
        CONSTRAINT FK_AgReplicas_Instance FOREIGN KEY (InstanceId) REFERENCES dbo.SqlInstances (InstanceId),
        CONSTRAINT UQ_AgReplicas_AgInstance UNIQUE (AgId, InstanceId)
    );
END
GO

-- ============================================================
-- Indexes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SqlInstances_EnvironmentId')
    CREATE NONCLUSTERED INDEX IX_SqlInstances_EnvironmentId ON dbo.SqlInstances (EnvironmentId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SqlDatabases_InstanceId')
    CREATE NONCLUSTERED INDEX IX_SqlDatabases_InstanceId ON dbo.SqlDatabases (InstanceId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgReplicas_AgId')
    CREATE NONCLUSTERED INDEX IX_AgReplicas_AgId ON dbo.AgReplicas (AgId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AgReplicas_InstanceId')
    CREATE NONCLUSTERED INDEX IX_AgReplicas_InstanceId ON dbo.AgReplicas (InstanceId);
GO

PRINT 'Schema created successfully.';
GO
