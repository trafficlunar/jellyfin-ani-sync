using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using jellyfin_ani_sync.Configuration;
using jellyfin_ani_sync.Models;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace jellyfin_ani_sync.Api;

public class GraphQlApiCall : AuthApiCall {
    protected GraphQlApiCall(ApiName provider, IHttpClientFactory httpClientFactory, IServerApplicationHost serverApplicationHost, IHttpContextAccessor httpContextAccessor, ILoggerFactory loggerFactory, UserConfig userConfig) :
        base(provider, httpClientFactory, serverApplicationHost, httpContextAccessor, loggerFactory, userConfig) {
    }

    protected async Task<HttpResponseMessage> AuthenticatedRequest(string query, ApiName provider, Dictionary<string, object> variables = null) {
        string url = string.Empty;
        switch (provider) {
            case ApiName.AniList:
                url = "https://graphql.anilist.co";
                break;
            case ApiName.Annict:
                url = "https://api.annict.com/graphql";
                break;
        }

        var call = await AuthenticatedApiCall(provider, AuthApiCall.CallType.POST, url, stringContent: new StringContent(JsonSerializer.Serialize(new GraphQl { Query = query, Variables = variables }), Encoding.UTF8, "application/json"));

        return call is { IsSuccessStatusCode: true } ? call : null;
    }

    protected static async Task<T> DeserializeRequest<T>(HttpClient httpClient, string query, Dictionary<string, object> variables) {
        var response = await Request(httpClient, query, variables);
        if (response != null) {
            StreamReader streamReader = new StreamReader(await response.Content.ReadAsStreamAsync());
            return JsonSerializer.Deserialize<T>(await streamReader.ReadToEndAsync());
        }

        return default;
    }
    
    private static async Task<HttpResponseMessage> Request(HttpClient httpClient, string query, Dictionary<string, object> variables = null) {
        var call = await httpClient.PostAsync("https://graphql.anilist.co", new StringContent(JsonSerializer.Serialize(new GraphQl { Query = query, Variables = variables }), Encoding.UTF8, "application/json"));

        return call.IsSuccessStatusCode ? call : null;
    }

    private class GraphQl {
        [JsonPropertyName("query")] public string Query { get; set; }
        [JsonPropertyName("variables")] public Dictionary<string, object> Variables { get; set; }
    }
}