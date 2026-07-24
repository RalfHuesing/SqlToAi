-- Creates the central AnonymizationRules table used by AnonymizationRuleProvider.
-- Unlike AnonymizerExclusions (which lives inside each customer database and is wiped out
-- by a customer backup restore), this table is meant to live in its own dedicated database,
-- configured independently via SqlToAi:AnonymizationRules in appsettings.json, so its rules
-- survive customer-side restores and apply consistently across many customer databases.
--
-- Pattern matching uses SQL LIKE wildcards (%, _) against DatabasePattern/TablePattern/ColumnPattern.
-- For a given (database, table, column), the most specific matching active rule wins:
-- specificity is scored per field (exact match > partial wildcard > pure '%'), weighted
-- DatabasePattern > TablePattern > ColumnPattern. A column with no matching rule is anonymized
-- by default (Anonymize = 1 behavior), so a database can be locked down to an allow-list by
-- simply never adding a broad wildcard rule for it.
-- [SchemaPattern] (optional, default '%' = "any schema") lets a rule be scoped to a single schema
-- via the same LIKE-wildcard semantics as DatabasePattern/TablePattern/ColumnPattern, so a
-- same-named table in a different schema (e.g. dbo.Kunden vs. Archiv.Kunden) never inherits a rule
-- meant for another schema (see tasks/audit-2026-07-24/02-anonymisierung-tokenisierung.md, Finding
-- "Ausschluss-/Regel-Abgleich ist schema-blind"). Existing rows keep '%' and therefore keep
-- matching every schema, exactly as before this column existed.
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
