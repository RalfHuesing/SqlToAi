using System.Reflection;
using Microsoft.Data.SqlClient;

namespace SqlToAi.Tests.TestSupport;

internal static class SqlExceptionFactory
{
    internal static SqlException Create(int number, string message, byte severity = 16, Exception? inner = null)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var errors = (SqlErrorCollection)typeof(SqlErrorCollection)
            .GetConstructor(flags, null, Type.EmptyTypes, null)!.Invoke([]);
        var error = (SqlError)typeof(SqlError).GetConstructor(flags, null,
            [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)], null)!
            .Invoke([number, (byte)0, severity, "TestServer", message, "", 0, inner!]);
        typeof(SqlErrorCollection).GetMethod("Add", flags)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetConstructor(flags, null,
            [typeof(string), typeof(SqlErrorCollection), typeof(Exception), typeof(Guid)], null)!
            .Invoke([message, errors, inner!, Guid.Empty]);
    }
}
