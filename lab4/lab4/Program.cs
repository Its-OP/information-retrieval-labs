using System.Text.RegularExpressions;
using lab4;
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
        options.Settings(s => s
            .Analysis(a => a
                .Analyzers(an => an
                    .Custom("custom_html_analyzer", ca => ca
                        .CharFilters("html_strip")
                        .Tokenizer("standard")
                        .Filters("asciifolding", "stop", "lowercase", "stemmer")
                    ))));
        
        options.Map<Log>(m =>
        {
            // m.AutoMap<Log>();
            m.Properties(p => p.Keyword(k => k.Name(log => log.Id)));
            m.Properties(p => p.Keyword(k => k.Name(log => log.Source)));
            m.Properties(p => p.Date(k => k.Name(log => log.CreatedAt)));
            
            m.Properties(p => p.Text(t => t.Name(log => log.ErrorMessage)));
            m.Properties(p => p.Text(t => t.Name(log => log.UserInput).Analyzer("ukrainian")));
            m.Properties(p => p.Text(t => t.Name(log => log.Html).Analyzer("custom_html_analyzer")));

            return m;
        });
        
        return options;
    });
}

var exampleMessages = new Dictionary<int, string>
{
    { 1, """create '{ "Source": "ProductService", "ErrorMessage": "Failed to parse HTML document", "Html": "<!DOCTYPE html><html lang=\"en\"><head> <meta charset=\"UTF-8\"> <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"> <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"> <title>Simple HTML Document</title></head><body> <h1>Hello, World!</h1> <p>This is a simple HTML document.</p></body></html>", "UserInput": "Сумарізуй сторінку в 2 речення", "CreatedAt": "2024-02-26T17:22:06.350Z" }'""" },
    { 2, """create '{ "Source": "ProductService", "ErrorMessage": "Page not found", "Html": "", "UserInput": "О чому ця сторінка?", "CreatedAt": "2024-02-26T17:22:06.350Z" }'""" },
    { 3, """create '{ "Source": "AnArbitraryHandlerWithAReallyLongName", "ErrorMessage": "Could not access the page: Unauthorized", "Html": "<!DOCTYPE html><html lang=\"en\"><head> <meta charset=\"UTF-8\"> <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"> <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"> <title>Unauthorized Access</title> <style> body { font-family: Arial, sans-serif; text-align: center; padding: 50px; } h1 { color: #d9534f; } p { color: #333; } </style></head><body> <h1>401 Unauthorized</h1> <p>You do not have permission to view this directory or page using the credentials that you supplied.</p></body></html>", "UserInput": "На які джерела посилається автор статті?", "CreatedAt": "2024-02-24T17:22:06.350Z" }'""" },
    { 4, "get userInput:match:посилатись" }
};

while (true)
{
    Console.WriteLine("Please enter a command:");
    var command = Console.ReadLine();
    if (int.TryParse(command, out var messageNr) && exampleMessages.TryGetValue(messageNr, out var message))
        command = message;
    
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

    var log = new Log(arguments.Source, arguments.ErrorMessage, arguments.Html, arguments.UserInput, arguments.CreatedAt, Guid.NewGuid());
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

        if (filter.CreatedAtGreaterThen != DateTime.MinValue || filter.CreatedAtLessThen != DateTime.MaxValue)
        {
            queries.Add(q.DateRange(selector =>
            {
                var range = selector.Field(log => log.CreatedAt);
                
                if (filter.CreatedAtGreaterThen != DateTime.MinValue)
                    range = range.GreaterThan(filter.CreatedAtGreaterThen);
                
                if (filter.CreatedAtLessThen != DateTime.MaxValue)
                    range = range.LessThan(filter.CreatedAtLessThen);

                return range;
            }));
        }
        
        if (!string.IsNullOrEmpty(filter.ErrorMessageFullText))
        {
            queries.Add(q.Match(t => t.Field(log => log.ErrorMessage).Query(filter.ErrorMessageFullText)));
        }
        
        if (!string.IsNullOrEmpty(filter.HtmlFullText))
        {
            queries.Add(q.Match(t => t.Field(log => log.Html).Query(filter.HtmlFullText).Analyzer("custom_html_analyzer")));
        }
        
        if (!string.IsNullOrEmpty(filter.UserInputFullText))
        {
            queries.Add(q.Match(t => t.Field(log => log.UserInput).Query(filter.UserInputFullText).Analyzer("ukrainian")));
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
        
    var createdAtLessParam = GetStringParamValue($"{nameof(Log.CreatedAt)}:less:", searchArguments);
    var createdAtLessParamDateTime = ToDateTimeOrDefault(createdAtLessParam, DateTime.MaxValue);
        
    var createdAtGreaterParam = GetStringParamValue($"{nameof(Log.CreatedAt)}:greater:", searchArguments);
    var createdAtGreaterParamDateTime = ToDateTimeOrDefault(createdAtGreaterParam, DateTime.MinValue);

    var errorMessageFullTextParamValue = GetStringParamValue($"{nameof(Log.ErrorMessage)}:match:", searchArguments);
    var htmlFullTextParamValue = GetStringParamValue($"{nameof(Log.Html)}:match:", searchArguments);
    var userInputTextParamValue = GetStringParamValue($"{nameof(Log.UserInput)}:match:", searchArguments);

    return new FilterValues(idKeywordParamGuid,
        sourceKeywordParam,
        sourceFuzzyParam,
        createdAtLessParamDateTime, 
        createdAtGreaterParamDateTime,
        errorMessageFullTextParamValue,
        htmlFullTextParamValue,
        userInputTextParamValue);
}

static string GetStringParamValue(string prefix, IEnumerable<string> searchArguments)
{
    var param = searchArguments.SingleOrDefault(x => x.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase));
    if (param is null)
        return string.Empty;

    return param.Replace(prefix, string.Empty, StringComparison.InvariantCultureIgnoreCase);
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
    DateTime CreatedAtLessThen,
    DateTime CreatedAtGreaterThen,
    string ErrorMessageFullText,
    string HtmlFullText,
    string UserInputFullText);
