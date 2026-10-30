using SqlToAi.Configuration;
using SqlToAi.Domain;

namespace SqlToAi.Database;

public interface IShowplanXmlParser
{
    Result<ExplainQueryResult> Parse(
        string showplanXml,
        string target,
        ExplainQueryOptions? options = null);
}

