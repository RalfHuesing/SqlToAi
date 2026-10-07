-- Creates the central rule table, configured via SqlToAi:AnonymizationRules.
-- Place it in a separate database when rules must survive customer database restores.
-- Patterns use SQL LIKE wildcards (%, _) across database, schema, table and column.
-- Rule resolution and protective conflict handling: ../docs/security.md#central-anonymization-rules.
-- SchemaPattern defaults to '%' for rules that apply to every schema.
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizationRules]') AND type IN (N'U'))
BEGIN
    CREATE TABLE [dbo].[AnonymizationRules] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [DatabasePattern] NVARCHAR(255) NOT NULL DEFAULT '%',
        [SchemaPattern] NVARCHAR(255) NOT NULL DEFAULT '%',
        [TablePattern] NVARCHAR(255) NOT NULL DEFAULT '%',
        [ColumnPattern] NVARCHAR(255) NOT NULL,
        [Anonymize] BIT NOT NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [Comment] NVARCHAR(500) NULL,
        [CreatedAtUtc] DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        [CreatedBy] NVARCHAR(128) NULL
    );
END
GO

-- Migration for installations that already had this table before [SchemaPattern] existed. The
-- DEFAULT '%' backfills every pre-existing row, so behavior is unchanged until an admin
-- deliberately narrows a specific rule to one schema.
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizationRules]') AND type IN (N'U'))
   AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[AnonymizationRules]') AND name = 'SchemaPattern')
BEGIN
    ALTER TABLE [dbo].[AnonymizationRules] ADD [SchemaPattern] NVARCHAR(255) NOT NULL DEFAULT '%';
END
GO

-- Sample rules for the fictional demo schema, illustrating both use cases from the design
-- discussion: (1) opening up a whole table except one column, and (2) an allow-list-only
-- database where only explicitly listed columns are ever shown in clear text.
IF NOT EXISTS (SELECT * FROM [dbo].[AnonymizationRules] WHERE [TablePattern] = 'FakeConsultants' AND [ColumnPattern] = '%')
BEGIN
    INSERT INTO [dbo].[AnonymizationRules] ([DatabasePattern], [TablePattern], [ColumnPattern], [Anonymize], [Comment], [CreatedBy])
    VALUES ('%', 'FakeConsultants', '%', 0, 'Consultant data is not sensitive by default across all customer databases.', 'Setup');
END

IF NOT EXISTS (SELECT * FROM [dbo].[AnonymizationRules] WHERE [TablePattern] = 'FakeConsultants' AND [ColumnPattern] = 'FullName')
BEGIN
    INSERT INTO [dbo].[AnonymizationRules] ([DatabasePattern], [TablePattern], [ColumnPattern], [Anonymize], [Comment], [CreatedBy])
    VALUES ('%', 'FakeConsultants', 'FullName', 1, 'Names stay anonymized even though the table is otherwise open (overrides the wildcard rule above).', 'Setup');
END

IF NOT EXISTS (SELECT * FROM [dbo].[AnonymizationRules] WHERE [DatabasePattern] = 'FakeHighSecurityDb' AND [ColumnPattern] = 'ContactEmail')
BEGIN
    INSERT INTO [dbo].[AnonymizationRules] ([DatabasePattern], [TablePattern], [ColumnPattern], [Anonymize], [Comment], [CreatedBy])
    VALUES ('FakeHighSecurityDb', '%', 'ContactEmail', 0, 'Explicit allow-list entry for an otherwise fully locked-down database (no wildcard rule exists for it).', 'Setup');
END
GO
