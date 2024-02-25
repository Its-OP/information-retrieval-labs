using lab3;
using Nest;
using Newtonsoft.Json;

const string indexName = "logs";

var client = new ElasticClient(new Uri("http://localhost:9200"));

if (!(await client.Indices.ExistsAsync(indexName)).Exists)
{
    await client.Indices.CreateAsync(indexName, options =>
    {
        options.Map<Log>(m =>
        {
            m.AutoMap<Log>();
            m.Properties(p => p.Keyword(k => k.Name(log => log.Source)));
            m.Properties(p => p.Text(k => k.Name(log => log.Body)));
            m.Properties(p => p.IntegerRange(k => k.Name(log => log.Level)));
            m.Properties(p => p.Date(k => k.Name(log => log.CreatedAt)));
            return m;
        });
        
        return options;
    });
}

while (true)
{
    Console.WriteLine("Please enter a command:");
    var command = Console.ReadLine() ?? string.Empty;
    var commandParts = command.Split();

    var response = (commandParts.First(), commandParts.Length) switch
    {
        ("create", 2) => await ProcessCreateDocument(client, JsonConvert.DeserializeObject<LogArguments>(commandParts[1])),
        ("get", >= 1 and <= 5) => await ProcessGetDocument(client, Guid.Parse(commandParts[1]), commandParts[1..]),
        ("delete", 2) => await ProcessDeleteDocument(client, Guid.Parse(commandParts[1])),
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

    return JsonConvert.SerializeObject(response);
}

static async Task<string> ProcessGetDocument(IElasticClient client, Guid logId, params string[] searchArguments)
{
    if (logId == Guid.Empty)
        return "Id is empty";

    var response = await client.SearchAsync<Log>(s => s.Index(indexName).Query(q =>
    {
        var keywordParam = searchArguments.SingleOrDefault(x =>
            x.StartsWith($"{nameof(Log.Source)}%exact", StringComparison.InvariantCultureIgnoreCase) && x.Contains(':'));
        if (keywordParam is not null)
        {
            var paramValue = keywordParam.Split(':').Last();
            q.Term(t => t.Field(log => log.Source).Value(paramValue));
        }
        
        var rangeLessParam = searchArguments.SingleOrDefault(x =>
            x.StartsWith(nameof(Log.Level), StringComparison.InvariantCultureIgnoreCase) && x.Contains(':') && x.Contains('<'));
        if (rangeLessParam is not null)
        {
            var paramValue = rangeLessParam.Split('<').Last();
            if (int.TryParse(paramValue, out var paramValueInt))
                q.Range(t => t.Field(log => log.Source).LessThan(paramValueInt));
        }
        
        var rangeGreaterParam = searchArguments.SingleOrDefault(x =>
            x.StartsWith(nameof(Log.Level), StringComparison.InvariantCultureIgnoreCase) && x.Contains(':') && x.Contains('>'));
        if (rangeGreaterParam is not null)
        {
            var paramValue = rangeGreaterParam.Split('>').Last();
            if (int.TryParse(paramValue, out var paramValueInt))
                q.Range(t => t.Field(log => log.Source).GreaterThan(paramValueInt));
        }
        
        var fuzzyParam = searchArguments.SingleOrDefault(x =>
            x.StartsWith($"{nameof(Log.Source)}%fuzzy", StringComparison.InvariantCultureIgnoreCase) && x.Contains(':'));
        if (fuzzyParam is not null)
        {
            var paramValue = fuzzyParam.Split(':').Last();
            q.Fuzzy(t => t.Field(log => log.Source).Value(paramValue));
        }

        return q;
    }));
    
    if (!response.IsValid)
        return "Failed to insert the new log";

    return JsonConvert.SerializeObject(response);
}

static async Task<string> ProcessDeleteDocument(IElasticClient client, Guid logId)
{
    if (logId == Guid.Empty)
        return "Id is empty";
    
    var response = await client.DeleteAsync<Log>(logId, i => i.Index(indexName));

    if (!response.IsValid)
        return "Failed to insert the new log";

    return JsonConvert.SerializeObject(response);
}
