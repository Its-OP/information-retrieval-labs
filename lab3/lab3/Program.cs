using System.Text.RegularExpressions;
using lab3;
using Nest;
using Newtonsoft.Json;

const string indexName = "logs";

var settings = new ConnectionSettings(new Uri("http://localhost:9200"));
settings.DisableDirectStreaming(false);

var client = new ElasticClient(settings);

if (!(await client.Indices.ExistsAsync(indexName)).Exists)
{
    await client.Indices.CreateAsync(indexName, options =>
    {
        options.Map<Log>(m =>
        {
            // m.AutoMap<Log>();
            m.Properties(p => p.Keyword(k => k.Name(log => log.Source)));
            m.Properties(p => p.Text(k => k.Name(log => log.Body)));
            m.Properties(p => p.Number(k => k.Name(log => log.Level)));
            m.Properties(p => p.Date(k => k.Name(log => log.CreatedAt)));
            return m;
        });
        
        return options;
    });
}

while (true)
{
    Console.WriteLine("Please enter a command:");
    var command = Console.ReadLine();
    var commandParts = MyRegex().Split(command ?? string.Empty).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim('\'')).ToArray();

    var response = (commandParts.Length, commandParts.FirstOrDefault()) switch
    {
        (2, "create") => await ProcessCreateDocument(client, JsonConvert.DeserializeObject<LogArguments>(commandParts[1])),
        (>= 1 and <= 5, "get") => await ProcessGetDocument(client, commandParts[1..]),
        (2, "delete") => await ProcessDeleteDocument(client, Guid.Parse(commandParts[1])),
        _ => "Unknown command"
    };
    
    Console.WriteLine($"{response}\n");
}

static async Task<string> ProcessCreateDocument(IElasticClient client, LogArguments? arguments)
{
    if (arguments is null)
        return "Failed to parse the log";

    var log = new Log(arguments.Source, arguments.Body, arguments.Level, DateTime.Now, Guid.NewGuid());
    var response = await client.IndexAsync(log, i => i.Index(indexName));

    if (!response.IsValid)
        return "Failed to insert the new log";

    return JsonConvert.SerializeObject(log);
}

static async Task<string> ProcessGetDocument(IElasticClient client, params string[] searchArguments)
{
    var response = await client.SearchAsync<Log>(s => s.Index(indexName).Query(q =>
    {
        var queries = new List<QueryContainer>();
        
        var keywordParam = searchArguments.SingleOrDefault(x =>
            x.StartsWith($"{nameof(Log.Source)}%exact", StringComparison.InvariantCultureIgnoreCase) &&
            x.Contains(':'));
        if (keywordParam is not null)
        {
            var paramValue = keywordParam.Split(':').Last();
            queries.Add(q.Term(t => t.Field(log => log.Source).Value(paramValue)));
        }

        var rangeLessParam = searchArguments
            .SingleOrDefault(x =>
                x.StartsWith(nameof(Log.Level), StringComparison.InvariantCultureIgnoreCase) && x.Contains(':') &&
                x.Contains('<'));
        var rangeGreaterParam = searchArguments
            .SingleOrDefault(x =>
                x.StartsWith(nameof(Log.Level), StringComparison.InvariantCultureIgnoreCase) && x.Contains(':') &&
                x.Contains('>'));

        queries.Add(q.Range(t =>
            t.Field(log => log.Level).LessThan(ExtractParamValue(rangeLessParam, '<', int.MaxValue))
                .GreaterThan(ExtractParamValue(rangeGreaterParam, '>', int.MinValue))));

        var fuzzyParam = searchArguments.SingleOrDefault(x =>
            x.StartsWith($"{nameof(Log.Source)}%fuzzy", StringComparison.InvariantCultureIgnoreCase) &&
            x.Contains(':'));
        if (fuzzyParam is not null)
        {
            var paramValue = fuzzyParam.Split(':').Last();
            queries.Add(q.Fuzzy(t => t.Field(log => log.Source).Value(paramValue).Fuzziness(Fuzziness.AutoLength(1, 5))));
        }

        var finalQuery = q as QueryContainer;
        foreach (var query in queries)
        {
            finalQuery = q && query;
        }

        return finalQuery;
    }));
    
    if (!response.IsValid)
        return "Failed to insert the new log";
    
    return JsonConvert.SerializeObject(response.Documents, Formatting.Indented);
}

static async Task<string> ProcessDeleteDocument(IElasticClient client, Guid logId)
{
    if (logId == Guid.Empty)
        return "Id is empty";
    
    var response = await client.DeleteAsync<Log>(logId, i => i.Index(indexName));

    if (!response.IsValid)
        return "Failed to insert the new log";

    return "Deleted successfully";
}

static int ExtractParamValue(string? param, char separator, int defaultValue)
{
    var paramValue = param?.Split(separator).Last();
    return int.TryParse(paramValue, out var intParamValue)
        ? intParamValue
        : defaultValue;
}

internal partial class Program
{
    [GeneratedRegex("""
                    ('.*?'|\S+)
                    """)]
    private static partial Regex MyRegex();
}