-- Create the AnonymizerExclusions table to store column-specific anonymization rules.
-- This table allows specifying columns that must NOT be anonymized on a per-database basis.
--
-- [SchemaName] (optional, NULL = "any schema") lets an exclusion be scoped to a single schema, so
-- a same-named table in a different schema (e.g. dbo.Kunden vs. Archiv.Kunden) never inherits an
-- exclusion meant for another schema (see tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md,
-- Finding "Ausschluss-/Regel-Abgleich ist schema-blind"). Existing rows keep NULL and therefore
-- keep matching every schema, exactly as before this column existed.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizerExclusions]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AnonymizerExclusions] (
        [TableName] NVARCHAR(255) NOT NULL,
        [ColumnName] NVARCHAR(255) NOT NULL,
        [SchemaName] NVARCHAR(255) NULL,
        CONSTRAINT [PK_AnonymizerExclusions] PRIMARY KEY CLUSTERED ([TableName] ASC, [ColumnName] ASC)
    );
END
GO

-- Migration for installations that already had this table before [SchemaName] existed. Adding it
-- as NULL-able is fully backward-compatible: every pre-existing row stays NULL ("any schema"), so
-- behavior is unchanged until an admin deliberately narrows a specific row to one schema.
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizerExclusions]') AND type in (N'U'))
   AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizerExclusions]') AND name = 'SchemaName')
BEGIN
    ALTER TABLE [dbo].[AnonymizerExclusions] ADD [SchemaName] NVARCHAR(255) NULL;
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
