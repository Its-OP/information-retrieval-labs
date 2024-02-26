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
            m.Properties(p => p.Keyword(k => k.Name(log => log.Id)));
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
        (>= 1, "get") => await ProcessGetDocument(client, commandParts[1..]),
        (2, "delete") => await ProcessDeleteDocument(client, Guid.Parse(commandParts[1])),
        _ => "Unknown command"
    };
    
    Console.WriteLine($"{response}\n");
}

static async Task<string> ProcessCreateDocument(IElasticClient client, LogArguments? arguments)
{
    if (arguments is null)
        return "Failed to parse the log";

    var log = new Log(arguments.Source, arguments.Body, arguments.Level, arguments.CreatedAt, Guid.NewGuid());
    var response = await client.IndexAsync(log, i => i.Index(indexName));

    if (!response.IsValid)
        return "Failed to insert the new log";

    return JsonConvert.SerializeObject(log);
}

static async Task<string> ProcessGetDocument(IElasticClient client, params string[] searchArguments)
{
    var response = await client.SearchAsync<Log>(s => s.Index(indexName).Query(q =>
    {
        var filter = ParseFilterValues(searchArguments);
        var queries = new List<QueryContainer>();
        
        if (filter.IdKeyword != Guid.Empty)
        {
            queries.Add(q.Term(t => t.Field(log => log.Id).Value(filter.IdKeyword)));
        }
        
        if (!string.IsNullOrEmpty(filter.SourceKeyword))
        {
            queries.Add(q.Term(t => t.Field(log => log.Source).Value(filter.SourceKeyword)));
        }
        
        if (!string.IsNullOrEmpty(filter.SourceFuzzy))
        {
            queries.Add(q.Fuzzy(f => f.Field(log => log.Source).Value(filter.SourceFuzzy)));
        }

        if (filter.LevelGreaterThen != int.MinValue || filter.LevelLessThen != int.MaxValue)
        {
            queries.Add(q.Range(selector =>
            {
                var range = selector.Field(log => log.Level);
                
                if (filter.LevelGreaterThen != int.MinValue)
                    range = range.GreaterThan(filter.LevelGreaterThen);
                
                if (filter.LevelLessThen != int.MaxValue)
                    range = range.LessThan(filter.LevelLessThen);

                return range;
            }));
        }

        if (filter.CreatedAtGreaterThen != DateTime.MinValue || filter.CreatedAtLessThen != DateTime.MaxValue)
        {
            queries.Add(q.DateRange(selector =>
            {
                var range = selector.Field(log => log.CreatedAt);
                
                if (filter.CreatedAtGreaterThen != DateTime.MinValue)
                    range = range.GreaterThan(filter.CreatedAtGreaterThen);
                
                if (filter.LevelLessThen != int.MaxValue)
                    range = range.LessThan(filter.CreatedAtLessThen);

                return range;
            }));
        }

        var finalQuery = q as QueryContainer;
        foreach (var query in queries)
        {
            finalQuery = q && query;
        }

        return finalQuery;
    }));
    
    if (!response.IsValid)
        return "Query failed";
    
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

static FilterValues ParseFilterValues(ICollection<string> searchArguments)
{
    var idKeywordParam = GetStringParamValue($"{nameof(Log.Id)}:exact:", searchArguments);
    var idKeywordParamGuid = ToGuidOrDefault(idKeywordParam, Guid.Empty);
    
    var sourceKeywordParam = GetStringParamValue($"{nameof(Log.Source)}:exact:", searchArguments);
    var sourceFuzzyParam = GetStringParamValue($"{nameof(Log.Source)}:fuzzy:", searchArguments);
        
    var levelLessParam = GetStringParamValue($"{nameof(Log.Level)}:less:", searchArguments);
    var levelLessParamInt = ToIntOrDefault(levelLessParam, int.MaxValue);
        
    var levelGreaterParam = GetStringParamValue($"{nameof(Log.Level)}:greater:", searchArguments);
    var levelGreaterParamInt = ToIntOrDefault(levelGreaterParam, int.MinValue);
        
    var createdAtLessParam = GetStringParamValue($"{nameof(Log.CreatedAt)}:less:", searchArguments);
    var createdAtLessParamDateTime = ToDateTimeOrDefault(createdAtLessParam, DateTime.MaxValue);
        
    var createdAtGreaterParam = GetStringParamValue($"{nameof(Log.CreatedAt)}:greater:", searchArguments);
    var createdAtGreaterParamDateTime = ToDateTimeOrDefault(createdAtGreaterParam, DateTime.MinValue);

    return new FilterValues(idKeywordParamGuid,
        sourceKeywordParam,
        sourceFuzzyParam, 
        levelLessParamInt, 
        levelGreaterParamInt,
        createdAtLessParamDateTime, 
        createdAtGreaterParamDateTime);
}

static string GetStringParamValue(string prefix, IEnumerable<string> searchArguments)
{
    var param = searchArguments.SingleOrDefault(x => x.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase));
    if (param is null)
        return string.Empty;

    return param.Replace(prefix, string.Empty, StringComparison.InvariantCultureIgnoreCase);
}

static int ToIntOrDefault(string value, int @default)
{
    if (int.TryParse(value, out var intValue))
        return intValue;

    return @default;
}

static DateTime ToDateTimeOrDefault(string value, DateTime @default)
{
    if (DateTime.TryParse(value, out var dateTimeValue))
        return dateTimeValue;

    return @default;
}

static Guid ToGuidOrDefault(string value, Guid @default)
{
    if (Guid.TryParse(value, out var guidValue))
        return guidValue;

    return @default;
}

internal partial class Program
{
    [GeneratedRegex("""
                    ('.*?'|\S+)
                    """)]
    private static partial Regex MyRegex();
}

internal record FilterValues(Guid IdKeyword,
    string SourceKeyword,
    string SourceFuzzy,
    int LevelLessThen,
    int LevelGreaterThen,
    DateTime CreatedAtLessThen,
    DateTime CreatedAtGreaterThen);
