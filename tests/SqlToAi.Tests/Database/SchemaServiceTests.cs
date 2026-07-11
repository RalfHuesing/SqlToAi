#nullable enable

using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Database;
using SqlToAi.Domain;
using SqlToAi.Metadata;
using SqlToAi.Security;
using Dapper;

namespace SqlToAi.Tests.Database;

#pragma warning disable CS8765

// @covers SqlToAi.Database.SchemaService
public sealed class SchemaServiceTests
{
    private static readonly Type TargetType = typeof(SchemaService);

    [Fact]
    public async Task ListDatabasesAsync_ShouldReturnAllowedDatabases_WhenQuerySucceeds()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.ListDatabasesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Contains("DemoDb", result.Value);
        Assert.Contains("SalesDb", result.Value);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldReturnSecurityError_WhenDbBlocked()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["SalesDb"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.SearchObjectsAsync("BlockedDb", "cust", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal(SqlToAiError.SafetyCheckFailedCode, result.Error.Code);
    }

    [Fact]
    public async Task SearchObjectsAsync_ShouldReturnMarkdownTable_WhenSucceeds()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.SearchObjectsAsync("DemoDb", "cust", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("Customers", result.Value);
        Assert.Contains("USER_TABLE", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnTableSchema_WhenObjectIsTable()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("# Schema for Table/View: `dbo.Customers`", result.Value);
        Assert.Contains("CustomerId", result.Value);
        Assert.Contains("trg_Audit", result.Value);
        Assert.Contains("Discovery Index", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldReturnRoutineSchema_WhenObjectIsProcedure()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync("DemoDb", "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("# DDL Definition for Stored Procedure/Function: `dbo.GetCustomersProc`", result.Value);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetSchemaAsync_ShouldIncludeViewDefinition_WhenObjectIsView()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];

        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaAsync("DemoDb", "dbo.CustomersView", TestContext.Current.CancellationToken);

        // Assert — views get both the column list (like tables) and their SQL body (like routines).
        Assert.True(result.IsSuccess);
        Assert.Contains("# Schema for Table/View: `dbo.CustomersView`", result.Value);
        Assert.Contains("CustomerId", result.Value);
        Assert.Contains("## View Definition", result.Value);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetSchemaForeignKeysAsync_ShouldReturnKeys()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaForeignKeysAsync("DemoDb", "dbo.Orders", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("FK_Orders_Customers", result.Value);
        Assert.Contains("dbo.Orders.CustomerId", result.Value);
    }

    [Fact]
    public async Task GetSchemaIndexesAsync_ShouldReturnIndexes()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaIndexesAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("PK_Customers", result.Value);
        Assert.Contains("CLUSTERED", result.Value);
    }

    [Fact]
    public async Task GetSchemaConstraintsAsync_ShouldReturnConstraints()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetSchemaConstraintsAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("DF_Customers_Created", result.Value);
        Assert.Contains("DEFAULT", result.Value);
    }

    [Fact]
    public async Task GetTriggerDefinitionAsync_ShouldReturnTriggerDDL()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetTriggerDefinitionAsync("DemoDb", "dbo.Customers", "trg_Audit", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("CREATE PROCEDURE GetCustomers", result.Value);
    }

    [Fact]
    public async Task GetObjectReferencesAsync_ShouldReturnReferences()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetObjectReferencesAsync("DemoDb", "dbo.Customers", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("GetCustomers", result.Value);
        Assert.Contains("OBJECT_OR_COLUMN", result.Value);
    }

    [Fact]
    public async Task GetRoutineParametersAsync_ShouldReturnParameters()
    {
        // Arrange
        var options = new SqlToAiOptions();
        options.Databases.Allowed = ["*"];
        
        var mockFactory = new DummyConnectionFactory();
        var securityGuard = new SecurityGuard(Options.Create(options));
        var accessLevelProvider = new AccessLevelProvider(mockFactory, Options.Create(options), NullLogger<AccessLevelProvider>.Instance);
        var metadataProvider = new MetadataProvider(mockFactory, Options.Create(options), NullLogger<MetadataProvider>.Instance);

        var service = new SchemaService(mockFactory, securityGuard, accessLevelProvider, metadataProvider, Options.Create(options), NullLogger<SchemaService>.Instance);

        // Act
        var result = await service.GetRoutineParametersAsync("DemoDb", "dbo.GetCustomersProc", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Contains("@CustomerId", result.Value);
        Assert.Contains("int", result.Value);
    }

    // Helper classes for mocking ADO.NET connections
    private sealed class DummyConnectionFactory : IDatabaseConnectionFactory
    {
        public int ConnectionCreatedCount { get; private set; }

        public DbConnection CreateConnection(string? databaseName = null)
        {
            ConnectionCreatedCount++;
            return new MockSchemaConnection();
        }
    }

    private sealed class MockSchemaConnection : DbConnection
    {
        private string _connectionString = "";

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString
        {
            get => _connectionString;
            set => _connectionString = value ?? string.Empty;
        }

        public override string Database => "MockDb";
        public override string DataSource => "MockServer";
        public override string ServerVersion => "1.0";
        public override ConnectionState State => ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        public override Task OpenAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override DbCommand CreateDbCommand()
        {
            return new MockSchemaCommand();
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MockSchemaCommand : DbCommand
    {
        private readonly DbParameterCollection _parameters = new MockParameterCollection();
        private DbConnection? _connection;

        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        protected override DbConnection? DbConnection
        {
            get => _connection;
            set => _connection = value;
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; } = UpdateRowSource.None;

        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        
        public override object? ExecuteScalar()
        {
            if (CommandText.Contains("sys.databases")) return "DemoDb";
            if (CommandText.Contains("sys.objects")) return "U";
            return 1;
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            if (CommandText.Contains("COUNT(*)"))
            {
                return new MockDataTableReader(["CountValue"], [[1]]);
            }
            if (CommandText.Contains("sys.databases"))
            {
                return new MockDataTableReader(["name"], [["DemoDb"], ["SalesDb"]]);
            }
            if (CommandText.Contains("sys.dm_sql_referencing_entities"))
            {
                return new MockDataTableReader(
                    ["SchemaName", "EntityName", "ClassDescription"],
                    [["dbo", "GetCustomers", "OBJECT_OR_COLUMN"]]
                );
            }
            if (CommandText.Contains("sys.foreign_keys"))
            {
                return new MockDataTableReader(
                    ["ForeignKeyName", "ParentTable", "ParentColumn", "ReferencedTable", "ReferencedColumn"],
                    [["FK_Orders_Customers", "dbo.Orders", "CustomerId", "dbo.Customers", "CustomerId"]]
                );
            }
            if (CommandText.Contains("sys.indexes") && (CommandText.Contains("is_included_column") || CommandText.Contains("type_desc")))
            {
                return new MockDataTableReader(
                    ["IndexName", "IndexType", "IsUnique", "IsPrimaryKey", "ColumnName", "IsIncluded"],
                    [["PK_Customers", "CLUSTERED", true, true, "CustomerId", false]]
                );
            }
            if (CommandText.Contains("sys.default_constraints") || CommandText.Contains("sys.check_constraints"))
            {
                return new MockDataTableReader(
                    ["ConstraintName", "ColumnName", "Definition", "ConstraintType"],
                    [["DF_Customers_Created", "CreatedDate", "(getdate())", "DEFAULT"]]
                );
            }
            if (CommandText.Contains("sys.triggers"))
            {
                return new MockDataTableReader(
                    ["TriggerName", "IsDisabled", "IsUpdate", "IsDelete", "IsInsert"],
                    [["trg_Audit", 0, 0, 0, 1]]
                );
            }
            if (CommandText.Contains("sys.parameters"))
            {
                return new MockDataTableReader(
                    ["ParameterName", "DataType", "MaxLength", "IsOutput"],
                    [["@CustomerId", "int", 4, false]]
                );
            }
            if (CommandText.Contains("sys.sql_modules"))
            {
                return new MockDataTableReader(["definition"], [["CREATE PROCEDURE GetCustomers AS SELECT * FROM Customers"]]);
            }
            if (CommandText.Contains("sys.columns"))
            {
                return new MockDataTableReader(
                    ["ColumnName", "DataType", "MaxLength", "Precision", "Scale", "IsNullable", "IsIdentity", "IsPrimaryKey"],
                    [["CustomerId", "int", 4, 10, 0, false, true, 1], ["Email", "varchar", 100, 0, 0, true, false, 0]]
                );
            }
            if (CommandText.Contains("sys.objects"))
            {
                if (CommandText.Contains("SELECT TOP")) // Search objects
                {
                    return new MockDataTableReader(
                        ["SchemaName", "ObjectName", "TypeDescription"],
                        [["dbo", "Customers", "USER_TABLE"]]
                    );
                }
                
                // Inspect parameters to determine object type dynamically
                bool isProc = false;
                bool isView = false;
                foreach (DbParameter p in _parameters)
                {
                    string? val = p.Value?.ToString();
                    if (val?.Contains("Proc", StringComparison.OrdinalIgnoreCase) == true) isProc = true;
                    if (val?.Contains("View", StringComparison.OrdinalIgnoreCase) == true) isView = true;
                }
                string typeCode = isProc ? "P" : isView ? "V" : "U";
                return new MockDataTableReader(["type"], [[typeCode]]);
            }

            return new MockDataTableReader(["value"], [[1]]);
        }

        protected override DbParameter CreateDbParameter() => new MockParameter();
        public override void Prepare() { }
    }

    private sealed class MockParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = new();

        public override int Count => _parameters.Count;
        public override object SyncRoot => this;
        public override bool IsReadOnly => false;
        public override bool IsFixedSize => false;

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var val in values)
            {
                Add(val!);
            }
        }

        public override void Clear() => _parameters.Clear();

        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);

        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);

        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);

        public override void Remove(object value) => _parameters.Remove((DbParameter)value);

        public override void RemoveAt(int index) => _parameters.RemoveAt(index);

        protected override DbParameter GetParameter(int index) => _parameters[index];

        protected override DbParameter GetParameter(string parameterName) =>
            _parameters.FirstOrDefault(p => p.ParameterName == parameterName) 
            ?? throw new KeyNotFoundException(parameterName);

        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            int idx = _parameters.FindIndex(p => p.ParameterName == parameterName);
            if (idx >= 0) _parameters[idx] = value;
            else _parameters.Add(value);
        }

        public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);

        public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);

        public override void RemoveAt(string parameterName)
        {
            int idx = _parameters.FindIndex(p => p.ParameterName == parameterName);
            if (idx >= 0) _parameters.RemoveAt(idx);
        }

        public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_parameters).CopyTo(array, index);

        public override System.Collections.IEnumerator GetEnumerator() => _parameters.GetEnumerator();
    }

    private sealed class MockParameter : DbParameter
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ParameterName { get; set; } = "";
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override object Value { get; set; } = DBNull.Value;
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        public override int Size { get; set; }
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override bool SourceColumnNullMapping { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class MockDataTableReader : DbDataReader
    {
        private readonly string[] _columns;
        private readonly List<object[]> _rows;
        private int _index = -1;

        public MockDataTableReader(string[] columns, List<object[]> rows)
        {
            _columns = columns;
            _rows = rows;
        }

        public override int FieldCount => _columns.Length;
        public override int Depth => 0;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool HasRows => _rows.Count > 0;

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            if (_index < _rows.Count - 1)
            {
                _index++;
                return true;
            }
            return false;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Read());
        }

        public override bool NextResult() => false;

        public override string GetName(int ordinal) => _columns[ordinal];
        public override int GetOrdinal(string name)
        {
            int idx = Array.FindIndex(_columns, c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) return idx;
#pragma warning disable CA2201 // Do not throw reserved exception types
            throw new IndexOutOfRangeException(name);
#pragma warning restore CA2201
        }

        public override object GetValue(int ordinal)
        {
            if (_index < 0 || _index >= _rows.Count)
            {
                return DBNull.Value;
            }
            return _rows[_index][ordinal];
        }

        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => "varchar";
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override Type GetFieldType(int ordinal)
        {
            if (_rows.Count > 0 && ordinal < _columns.Length)
            {
                var val = _rows[0][ordinal];
                return val?.GetType() ?? typeof(string);
            }
            return typeof(string);
        }
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
        public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
        public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
        public override string GetString(int ordinal) => GetValue(ordinal)?.ToString() ?? "";
        public override int GetValues(object[] values)
        {
            int count = Math.Min(FieldCount, values.Length);
            for (int i = 0; i < count; i++)
            {
                values[i] = GetValue(i);
            }
            return count;
        }
        public override bool IsDBNull(int ordinal) => GetValue(ordinal) == null || GetValue(ordinal) == DBNull.Value;

        public override System.Collections.IEnumerator GetEnumerator() => _rows.GetEnumerator();
    }
}
