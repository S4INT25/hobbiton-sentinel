namespace Sentinel.Infrastructure;

public class ClickHouseClient(HttpClient http, IConfiguration config, ILogger<ClickHouseClient> logger)
{
    private static readonly HashSet<string> AllowedPrefixes = ["SELECT", "WITH"];

    public async Task<string> QueryAsync(string sql)
    {
        // Safety: only allow read queries
        var trimmed = sql.TrimStart();
        var firstWord = trimmed.Split(' ', '\n', '\r')[0].ToUpperInvariant();

        if (!AllowedPrefixes.Contains(firstWord))
        {
            logger.LogWarning("Blocked non-SELECT query: {Query}", sql[..Math.Min(100, sql.Length)]);
            return "Error: Only SELECT/WITH queries are permitted.";
        }

        try
        {
            var host = config["ClickHouse:Host"]!;
            var user = config["ClickHouse:User"]!;
            var password = config["ClickHouse:Password"]!;
            var maxLength = config.GetValue("ClickHouse:MaxResultLength", 10000);

            // Use POST to avoid URL length limits on complex queries
            var url = $"{host}/?default_format=JSON";

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(sql)
            };
            request.Headers.Add("X-ClickHouse-User", user);
            request.Headers.Add("X-ClickHouse-Key", password);

            var response = await http.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("ClickHouse error {Status}: {Content}", response.StatusCode, content[..Math.Min(300, content.Length)]);
                return $"ClickHouse error ({response.StatusCode}): {content[..Math.Min(300, content.Length)]}";
            }

            // Truncate large results to protect context window
            if (content.Length > maxLength)
            {
                content = content[..maxLength] + $"\n\n[TRUNCATED — result exceeded {maxLength} chars. Consider adding LIMIT or narrowing your query.]";
            }
            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ClickHouse query failed");
            return $"Query failed: {ex.Message}";
        }
    }
}
