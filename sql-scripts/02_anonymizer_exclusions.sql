-- Create the AnonymizerExclusions table to store column-specific anonymization rules.
-- This table allows specifying columns that must NOT be anonymized on a per-database basis.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizerExclusions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AnonymizerExclusions] (
        [TableName] NVARCHAR(255) NOT NULL,
        [ColumnName] NVARCHAR(255) NOT NULL,
        CONSTRAINT [PK_AnonymizerExclusions] PRIMARY KEY CLUSTERED ([TableName] ASC, [ColumnName] ASC)
    );
END
GO

-- Insert sample exclusions for the fictional schema if it exists
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FakeProjects]') AND type in (N'U'))
BEGIN
    -- We exclude FakeProjects.ProjectName from anonymization to test db-specific exceptions.
    IF NOT EXISTS (SELECT * FROM [dbo].[AnonymizerExclusions] WHERE [TableName] = 'FakeProjects' AND [ColumnName] = 'ProjectName')
    BEGIN
        INSERT INTO [dbo].[AnonymizerExclusions] ([TableName], [ColumnName])
        VALUES ('FakeProjects', 'ProjectName');
    END
END
GO
