using lab3;
using Nest;

const string indexName = "logs";
var client = new ElasticClient(new Uri("http://localhost:9200"));

if (!(await client.Indices.ExistsAsync(indexName)).Exists)
{
    await client.Indices.CreateAsync(indexName, options =>
    {
        options.Map<Log>(m =>
        {
            m.Properties(p => p.Keyword(k => k.Name(log => log.Id)));
            m.Properties(p => p.Text(k => k.Name(log => log.Body)));
            m.Properties(p => p.IntegerRange(k => k.Name(log => log.Level)));
            m.Properties(p => p.Date(k => k.Name(log => log.CreatedAt)));
            return m;
        });
        
        return options;
    });
}
