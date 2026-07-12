-- =========================================================================
-- Fictional Schema Setup for SqlToAi Integration Tests
-- Safe to be run repeatedly (idempotent/incremental)
-- =========================================================================

-- 1. Table: dbo.FakeProjects
IF OBJECT_ID(N'[dbo].[FakeProjects]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FakeProjects] (
        [ProjectId] INT IDENTITY(1,1) PRIMARY KEY,
        [ProjectName] NVARCHAR(100) NOT NULL,
        [Mandant] INT NOT NULL,
        [Description] NVARCHAR(MAX) NULL,
        [StartDate] DATETIME NULL,
        [Status] NVARCHAR(50) NULL
    );
END
GO

-- Seed: dbo.FakeProjects
IF NOT EXISTS (SELECT 1 FROM [dbo].[FakeProjects])
BEGIN
    INSERT INTO [dbo].[FakeProjects] (ProjectName, Mandant, Description, StartDate, Status)
    VALUES 
    (N'Fictional Alpha Project', 1, N'This is a fictional alpha project for testing.', '2026-01-01T08:00:00', N'Active'),
    (N'Fictional Beta Project', 1, N'Another mock project for schema verification.', '2026-02-15T09:00:00', N'Planning'),
    (N'Fictional Gamma Project', 2, N'A completed test project.', '2026-03-10T10:00:00', N'Completed');
END
GO

-- 2. View: dbo.vewFakeProjectList
-- Using CREATE OR ALTER (supported in SQL Server 2016+)
CREATE OR ALTER VIEW [dbo].[vewFakeProjectList] AS
SELECT 
    [ProjectId],
    [ProjectName],
    [Mandant],
    [Status]
FROM [dbo].[FakeProjects];
GO

-- 3. Table: dbo.FakeContacts
IF OBJECT_ID(N'[dbo].[FakeContacts]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FakeContacts] (
        [ContactId] INT IDENTITY(1,1) PRIMARY KEY,
        [Name] NVARCHAR(100) NOT NULL,
        [Email] NVARCHAR(100) NOT NULL,
        [Ausfuehrer] NVARCHAR(50) NULL
    );
END
GO

-- Seed: dbo.FakeContacts
IF NOT EXISTS (SELECT 1 FROM [dbo].[FakeContacts])
BEGIN
    INSERT INTO [dbo].[FakeContacts] (Name, Email, Ausfuehrer)
    VALUES 
    (N'John Doe', N'john.doe@example.com', N'John'),
    (N'Jane Smith', N'jane.smith@example.com', N'Jane');
END
GO

-- 4. Table: dbo.FakeAddresses
IF OBJECT_ID(N'[dbo].[FakeAddresses]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FakeAddresses] (
        [Adresse] INT NOT NULL,
        [Mandant] INT NOT NULL,
        [Street] NVARCHAR(100) NULL,
        [City] NVARCHAR(50) NULL,
        PRIMARY KEY ([Adresse], [Mandant])
    );
END
GO

-- Seed: dbo.FakeAddresses
IF NOT EXISTS (SELECT 1 FROM [dbo].[FakeAddresses])
BEGIN
    INSERT INTO [dbo].[FakeAddresses] (Adresse, Mandant, Street, City)
    VALUES 
    (100, 1, N'123 Fictional Lane', N'Springfield'),
    (200, 1, N'456 Mockingbird Road', N'Shelbyville');
END
GO

-- 5. Table: dbo.FakeAddressCommunications
IF OBJECT_ID(N'[dbo].[FakeAddressCommunications]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[FakeAddressCommunications] (
        [CommunicationId] INT IDENTITY(1,1) PRIMARY KEY,
        [Adresse] INT NOT NULL,
        [Mandant] INT NOT NULL,
        [CommunicationValue] NVARCHAR(100) NULL,
        CONSTRAINT [FK_FakeAddressCommunications_FakeAddresses] FOREIGN KEY ([Adresse], [Mandant]) 
            REFERENCES [dbo].[FakeAddresses] ([Adresse], [Mandant])
    );
END
GO

-- Seed: dbo.FakeAddressCommunications
IF NOT EXISTS (SELECT 1 FROM [dbo].[FakeAddressCommunications])
BEGIN
    INSERT INTO [dbo].[FakeAddressCommunications] (Adresse, Mandant, CommunicationValue)
    VALUES 
    (100, 1, N'+1-555-0100'),
    (200, 1, N'+1-555-0200');
END
GO

-- 6. Stored Procedure: dbo.spFakeSysTan
CREATE OR ALTER PROCEDURE [dbo].[spFakeSysTan]
    @TanType VARCHAR(20),
    @NextValue INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    -- Just a mockup behavior returning a static value based on TanType
    IF @TanType = 'PJM'
    BEGIN
        SET @NextValue = 9999;
    END
    ELSE
    BEGIN
        SET @NextValue = 1111;
    END
END
GO

-- Grant permissions to Agent user so integration tests can see and run the procedure
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = 'Agent')
BEGIN
    GRANT EXECUTE ON [dbo].[spFakeSysTan] TO [Agent];
    GRANT VIEW DEFINITION ON [dbo].[spFakeSysTan] TO [Agent];
END
GO

