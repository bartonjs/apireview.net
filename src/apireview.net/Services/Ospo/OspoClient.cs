using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ApiReviewDotNet.Services.Ospo;

public sealed class OspoClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public OspoClient(string token)
    {
        if (token is null)
            throw new ArgumentNullException(nameof(token));

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://repos.opensource.microsoft.com/api/")
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("api-version", "2019-10-01");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($":{token}")));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<OspoLinkSet> GetAllAsync()
    {
        IReadOnlyList<OspoLink>? links = await GetAsJsonAsync<IReadOnlyList<OspoLink>>($"people/links");
        return links is null ? OspoLinkSet.Empty : new OspoLinkSet(links);
    }

    private async Task<T?> GetAsJsonAsync<T>(string requestUri)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        HttpResponseMessage response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        if (!response.IsSuccessStatusCode)
        {
            string message = await response.Content.ReadAsStringAsync();
            throw new Exception(message) { HResult = (int)response.StatusCode };
        }

        Stream responseStream = await response.Content.ReadAsStreamAsync();

        JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return await JsonSerializer.DeserializeAsync<T>(responseStream, options);
    }
}
