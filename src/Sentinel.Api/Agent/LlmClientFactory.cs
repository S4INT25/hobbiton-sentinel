using System.ClientModel;
using System.Collections.Concurrent;
using OpenAI;

namespace Sentinel.Agent;

/// <summary>
/// Caches <see cref="OpenAIClient"/> instances keyed by endpoint + api key so that
/// switching providers at runtime does not create a new pipeline per request.
/// </summary>
public class LlmClientFactory
{
    private readonly ConcurrentDictionary<string, OpenAIClient> _clients = new();

    public OpenAIClient GetOrCreate(string endpoint, string apiKey)
    {
        var key = $"{endpoint}|{apiKey.GetHashCode():X}";
        return _clients.GetOrAdd(key, _ => new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) }));
    }
}
