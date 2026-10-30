using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SqlToAi.Configuration;
using SqlToAi.Domain;
using SqlToAi.Mcp;
using SqlToAi.Security;

namespace SqlToAi.Database;

public sealed partial class ShowplanXmlParser : IShowplanXmlParser
{
    private const int MaxWarningsLimit = 25;

    private static readonly XmlReaderSettings SecureReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 1024,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false,
    };

    [GeneratedRegex(@"\[(?<part>[^\]]+)\](?:\.\[(?<part>[^\]]+)\])*", RegexOptions.None, matchTimeoutMilliseconds: SecurityConstants.DefaultRegexTimeoutMs)]
    private static partial Regex BracketedIdentifierRegex();

    private readonly ExplainQueryOptions _defaultOptions;

    public ShowplanXmlParser(IOptions<SqlToAiOptions>? options = null)
    {
        _defaultOptions = options?.Value.ExplainQuery ?? new ExplainQueryOptions();
    }

    public Result<ExplainQueryResult> Parse(
        string showplanXml,
        string target,
        ExplainQueryOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(showplanXml))
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError("Showplan XML is empty."));
        }

        var effectiveOptions = options ?? _defaultOptions;
        string canonicalTarget = target ?? string.Empty;

        XDocument doc;
        try
        {
            using var stringReader = new StringReader(showplanXml);
            using var xmlReader = XmlReader.Create(stringReader, SecureReaderSettings);
            doc = XDocument.Load(xmlReader);
        }
        catch (XmlException ex)
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError($"Malformed or prohibited showplan XML: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError($"Failed to parse showplan XML: {ex.Message}"));
        }

        if (doc.Root == null || !string.Equals(doc.Root.Name.LocalName, "ShowPlanXML", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError("Showplan XML missing root <ShowPlanXML> element."));
        }

        var selectStmt = FindSelectStatement(doc);
        if (selectStmt == null)
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError("Showplan XML does not contain an execution plan for a SELECT statement."));
        }

        var queryPlan = selectStmt.Elements().FirstOrDefault(e => e.Name.LocalName == "QueryPlan")
                        ?? selectStmt.Descendants().FirstOrDefault(e => e.Name.LocalName == "QueryPlan");
        if (queryPlan == null)
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError("Showplan XML does not contain a <QueryPlan> in the SELECT statement."));
        }

        var rootRelOp = queryPlan.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "RelOp" && !e.Ancestors().Any(a => a.Name.LocalName == "RelOp"));
        if (rootRelOp == null)
        {
            return Result<ExplainQueryResult>.Failure(
                SqlToAiError.QueryError("Showplan XML does not contain any <RelOp> operators in the QueryPlan."));
        }

        var (warnings, warningsTruncated) = ExtractWarnings(queryPlan);
        bool truncated = warningsTruncated;

        var (nodes, treeTruncated) = BuildOperatorTree(rootRelOp, effectiveOptions);
        if (treeTruncated)
        {
            truncated = true;
        }

        var result = ApplyByteLimitTruncation(canonicalTarget, nodes, warnings, truncated, effectiveOptions);
        return Result<ExplainQueryResult>.Success(result);
    }

    private static XElement? FindSelectStatement(XDocument doc)
    {
        var statements = doc.Descendants()
            .Where(e => e.Name.LocalName == "StmtSimple")
            .ToList();

        var selectStmt = statements.FirstOrDefault(s =>
            string.Equals(s.Attribute("StatementType")?.Value, "SELECT", StringComparison.OrdinalIgnoreCase) &&
            HasRelOps(s));

        if (selectStmt != null)
        {
            return selectStmt;
        }

        return statements.LastOrDefault(HasRelOps);
    }

    private static bool HasRelOps(XElement stmt) =>
        stmt.Descendants().Any(d => d.Name.LocalName == "RelOp");

    private static (List<ExplainPlanWarning> Warnings, bool Truncated) ExtractWarnings(XElement queryPlan)
    {
        var warnings = new List<ExplainPlanWarning>();

        foreach (var mi in queryPlan.Descendants().Where(e => e.Name.LocalName == "MissingIndex"))
        {
            string? rawTable = mi.Attribute("Table")?.Value;
            string? rawSchema = mi.Attribute("Schema")?.Value;
            string? schema = CleanIdentifier(rawSchema);
            string? table = CleanIdentifier(rawTable);

            if (string.IsNullOrEmpty(schema) && table != null && table.Contains('.', StringComparison.Ordinal))
            {
                var parts = table.Split('.', 2);
                schema = parts[0];
                table = parts[1];
            }

            var group = mi.Ancestors().FirstOrDefault(a => a.Name.LocalName == "MissingIndexGroup") ?? mi.Parent;
            string? impactStr = group?.Attribute("Impact")?.Value;
            double? impact = double.TryParse(impactStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double imp)
                ? imp
                : null;

            var keyColumns = new List<string>();
            var includeColumns = new List<string>();

            foreach (var cg in mi.Elements().Where(e => e.Name.LocalName == "ColumnGroup"))
            {
                string usage = cg.Attribute("Usage")?.Value ?? string.Empty;
                var cols = cg.Elements()
                    .Where(e => e.Name.LocalName == "Column")
                    .Select(c => CleanIdentifier(c.Attribute("Name")?.Value))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToList();

                if (string.Equals(usage, "EQUALITY", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(usage, "INEQUALITY", StringComparison.OrdinalIgnoreCase))
                {
                    keyColumns.AddRange(cols);
                }
                else if (string.Equals(usage, "INCLUDE", StringComparison.OrdinalIgnoreCase))
                {
                    includeColumns.AddRange(cols);
                }
            }

            warnings.Add(new ExplainPlanWarning(
                Kind: "missing_index",
                Schema: schema,
                Table: table,
                Impact: impact,
                KeyColumns: keyColumns.Count > 0 ? keyColumns : null,
                IncludeColumns: includeColumns.Count > 0 ? includeColumns : null));
        }

        foreach (var pac in queryPlan.Descendants().Where(e => e.Name.LocalName == "PlanAffectingConvert"))
        {
            string? convertIssue = pac.Attribute("ConvertIssue")?.Value;
            string? expr = pac.Attribute("Expression")?.Value;
            string? column = ExtractColumnFromExpression(expr);

            warnings.Add(new ExplainPlanWarning(
                Kind: "plan_affecting_convert",
                ConversionKind: convertIssue,
                Column: column));
        }

        bool truncated = false;
        if (warnings.Count > MaxWarningsLimit)
        {
            warnings = warnings.Take(MaxWarningsLimit).ToList();
            truncated = true;
        }

        return (warnings, truncated);
    }

    internal static string? ExtractColumnFromExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var match = BracketedIdentifierRegex().Match(expression);
        if (match.Success)
        {
            var parts = match.Groups["part"].Captures
                .Select(c => c.Value.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .ToList();

            if (parts.Count > 0)
            {
                return string.Join(".", parts);
            }
        }

        return null;
    }

    private static (List<PlanNodeBuilder> Nodes, bool Truncated) BuildOperatorTree(
        XElement rootRelOp,
        ExplainQueryOptions options)
    {
        int maxOperators = Math.Max(1, options.MaxOperators);
        int maxDepth = Math.Max(1, options.MaxDepth);

        var acceptedNodes = new List<PlanNodeBuilder>();
        bool truncated = false;

        var rootBuilder = CreateNodeBuilder(rootRelOp, depth: 1, parentId: null, parent: null);
        var queue = new Queue<PlanNodeBuilder>();
        queue.Enqueue(rootBuilder);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (acceptedNodes.Count >= maxOperators)
            {
                truncated = true;
                if (current.Parent != null)
                {
                    current.Parent.OmittedChildren++;
                }

                while (queue.Count > 0)
                {
                    var remaining = queue.Dequeue();
                    if (remaining.Parent != null)
                    {
                        remaining.Parent.OmittedChildren++;
                    }
                }
                break;
            }

            acceptedNodes.Add(current);

            var childElements = GetDirectChildRelOps(current.Element).ToList();
            foreach (var childElem in childElements)
            {
                if (current.Depth + 1 > maxDepth)
                {
                    current.OmittedChildren++;
                    truncated = true;
                }
                else
                {
                    var childBuilder = CreateNodeBuilder(
                        childElem,
                        depth: current.Depth + 1,
                        parentId: current.NodeId,
                        parent: current);
                    queue.Enqueue(childBuilder);
                }
            }
        }

        return (acceptedNodes, truncated);
    }

    private static IEnumerable<XElement> GetDirectChildRelOps(XElement relOp)
    {
        foreach (var descendant in relOp.Descendants())
        {
            if (descendant.Name.LocalName == "RelOp")
            {
                var parentRelOp = descendant.Ancestors().FirstOrDefault(a => a.Name.LocalName == "RelOp");
                if (parentRelOp == relOp)
                {
                    yield return descendant;
                }
            }
        }
    }

    private static PlanNodeBuilder CreateNodeBuilder(
        XElement relOp,
        int depth,
        int? parentId,
        PlanNodeBuilder? parent)
    {
        int nodeId = int.TryParse(relOp.Attribute("NodeId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            ? id
            : 0;

        string physicalOp = relOp.Attribute("PhysicalOp")?.Value ?? string.Empty;
        string logicalOp = relOp.Attribute("LogicalOp")?.Value ?? string.Empty;

        double estimatedRows = double.TryParse(relOp.Attribute("EstimateRows")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double er)
            ? er
            : 0.0;

        double subtreeCost = double.TryParse(relOp.Attribute("EstimatedTotalSubtreeCost")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sc)
            ? sc
            : 0.0;

        var obj = ExtractObject(relOp);

        return new PlanNodeBuilder
        {
            NodeId = nodeId,
            ParentId = parentId,
            PhysicalOp = physicalOp,
            LogicalOp = logicalOp,
            EstimatedRows = estimatedRows,
            SubtreeCost = subtreeCost,
            Object = obj,
            Depth = depth,
            Element = relOp,
            Parent = parent,
        };
    }

    private static ExplainPlanObject? ExtractObject(XElement relOp)
    {
        var directObjects = relOp.Descendants()
            .Where(e => e.Name.LocalName == "Object" &&
                        e.Ancestors().FirstOrDefault(a => a.Name.LocalName == "RelOp") == relOp)
            .ToList();

        if (directObjects.Count != 1)
        {
            return null;
        }

        var objElem = directObjects[0];
        string? rawTable = objElem.Attribute("Table")?.Value;
        if (string.IsNullOrWhiteSpace(rawTable))
        {
            return null;
        }

        string? rawSchema = objElem.Attribute("Schema")?.Value;
        string? rawIndex = objElem.Attribute("Index")?.Value;

        string? schema = CleanIdentifier(rawSchema);
        string table = CleanIdentifier(rawTable)!;
        string? index = CleanIdentifier(rawIndex);

        if (string.IsNullOrEmpty(schema) && table.Contains('.', StringComparison.Ordinal))
        {
            var parts = table.Split('.', 2);
            schema = parts[0];
            table = parts[1];
        }

        return new ExplainPlanObject(
            Schema: string.IsNullOrEmpty(schema) ? null : schema,
            Table: table,
            Index: string.IsNullOrEmpty(index) ? null : index);
    }

    private static string? CleanIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Replace("[", string.Empty, StringComparison.Ordinal)
                    .Replace("]", string.Empty, StringComparison.Ordinal)
                    .Trim();
    }

    private static ExplainQueryResult ApplyByteLimitTruncation(
        string target,
        List<PlanNodeBuilder> acceptedNodes,
        List<ExplainPlanWarning> warnings,
        bool initiallyTruncated,
        ExplainQueryOptions options)
    {
        bool truncated = initiallyTruncated;
        int maxBytes = Math.Max(512, options.MaxResponseBytes);

        var nodes = acceptedNodes.Select(b => b.ToNode()).ToList();
        var result = new ExplainQueryResult(
            Target: target,
            PlanKind: "estimated",
            Truncated: truncated,
            Nodes: nodes,
            Warnings: warnings);

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, McpJsonContext.Default.ExplainQueryResult);
        if (bytes.Length <= maxBytes)
        {
            return result;
        }

        truncated = true;

        while (bytes.Length > maxBytes && acceptedNodes.Count > 0)
        {
            var dropped = acceptedNodes[^1];
            acceptedNodes.RemoveAt(acceptedNodes.Count - 1);

            if (dropped.ParentId.HasValue)
            {
                var parent = acceptedNodes.FirstOrDefault(n => n.NodeId == dropped.ParentId.Value);
                if (parent != null)
                {
                    parent.OmittedChildren++;
                }
            }

            nodes = acceptedNodes.Select(b => b.ToNode()).ToList();
            result = new ExplainQueryResult(
                Target: target,
                PlanKind: "estimated",
                Truncated: truncated,
                Nodes: nodes,
                Warnings: warnings);

            bytes = JsonSerializer.SerializeToUtf8Bytes(result, McpJsonContext.Default.ExplainQueryResult);
        }

        while (bytes.Length > maxBytes && warnings.Count > 0)
        {
            warnings.RemoveAt(warnings.Count - 1);
            result = new ExplainQueryResult(
                Target: target,
                PlanKind: "estimated",
                Truncated: truncated,
                Nodes: nodes,
                Warnings: warnings);

            bytes = JsonSerializer.SerializeToUtf8Bytes(result, McpJsonContext.Default.ExplainQueryResult);
        }

        return result;
    }

    private sealed class PlanNodeBuilder
    {
        public required int NodeId { get; init; }
        public required int? ParentId { get; init; }
        public required string PhysicalOp { get; init; }
        public required string LogicalOp { get; init; }
        public required double EstimatedRows { get; init; }
        public required double SubtreeCost { get; init; }
        public ExplainPlanObject? Object { get; init; }
        public int OmittedChildren { get; set; }
        public required int Depth { get; init; }
        public required XElement Element { get; init; }
        public PlanNodeBuilder? Parent { get; init; }

        public ExplainPlanNode ToNode() => new(
            NodeId: NodeId,
            ParentId: ParentId,
            PhysicalOp: PhysicalOp,
            LogicalOp: LogicalOp,
            EstimatedRows: EstimatedRows,
            SubtreeCost: SubtreeCost,
            Object: Object,
            OmittedChildren: OmittedChildren > 0 ? OmittedChildren : null);
    }
}
