using System.Text.RegularExpressions;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Infrastructure;

public class ClickHouseClient(HttpClient http, IConfiguration config, ILogger<ClickHouseClient> logger, IFusionCache cache)
{
    private static readonly HashSet<string> AllowedPrefixes = ["SELECT", "WITH", "SHOW", "DESCRIBE", "DESC", "EXPLAIN"];
    private static readonly HashSet<string> CacheablePrefixes = ["SHOW", "DESCRIBE", "DESC"];
    private static readonly Regex StripLimit = new(@"\s+LIMIT\s+\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string> QueryAsync(string sql)
    {
        var trimmed = sql.TrimStart();
        var firstWord = trimmed.Split(' ', '\n', '\r')[0].ToUpperInvariant();

        if (!AllowedPrefixes.Contains(firstWord))
        {
            logger.LogWarning("Blocked non-SELECT query: {Query}", sql[..Math.Min(100, sql.Length)]);
            return "Error: Only SELECT/WITH/SHOW/DESCRIBE queries are permitted.";
        }

        if (CacheablePrefixes.Contains(firstWord))
        {
            sql = StripLimit.Replace(sql, "");
            var key = $"sentinel:schema:{sql.Trim().ToLowerInvariant().GetHashCode()}";
            return await cache.GetOrSetAsync(key,
                _ => ExecuteAsync(sql),
                options => options.SetDuration(TimeSpan.FromDays(7)));
        }

        return await ExecuteAsync(sql);
    }

    private async Task<string> ExecuteAsync(string sql)
    {
        try
        {
            var host = config["ClickHouse:Host"]!;
            var user = config["ClickHouse:User"]!;
            var password = config["ClickHouse:Password"]!;
            var maxLength = config.GetValue("ClickHouse:MaxResultLength", 10000);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{host}/?default_format=JSON")
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
            
            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ClickHouse query failed");
            return $"Query failed: {ex.Message}";
        }
    }
}
