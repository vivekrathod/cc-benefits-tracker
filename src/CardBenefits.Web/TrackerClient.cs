using System.Net.Http.Json;
using CardBenefits.Contracts;
using System.Text;
using System.Text.Json;

namespace CardBenefits.Web;

/// <summary>The web app's only way to the API. A refused request surfaces as its message.</summary>
public sealed class TrackerClient(HttpClient http)
{
    /// <summary>Raised after any change, so the layout can refresh the counts in the navigation.</summary>
    public event Action? Changed;

    public async Task<T> GetAsync<T>(string url)
    {
        using var response = await http.GetAsync(url);
        await EnsureAsync(response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task PostAsync(string url, object? body = null)
    {
        using var response = await http.PostAsJsonAsync(url, body ?? new { });
        await EnsureAsync(response);
        Changed?.Invoke();
    }

    public async Task<T> PostAsync<T>(string url, object? body = null)
    {
        using var response = await http.PostAsJsonAsync(url, body ?? new { });
        await EnsureAsync(response);
        Changed?.Invoke();
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task PutAsync(string url, object body)
    {
        using var response = await http.PutAsJsonAsync(url, body);
        await EnsureAsync(response);
        Changed?.Invoke();
    }

    public async Task DeleteAsync(string url)
    {
        using var response = await http.DeleteAsync(url);
        await EnsureAsync(response);
        Changed?.Invoke();
    }

    public async Task<ImportResultDto> UploadStatementAsync(Guid cardId, string csv)
    {
        using var response = await http.PostAsync($"api/cards/{cardId}/statements", new StringContent(csv, Encoding.UTF8, "text/csv"));
        await EnsureAsync(response);
        Changed?.Invoke();
        return (await response.Content.ReadFromJsonAsync<ImportResultDto>())!;
    }

    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? detail = null;
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (problem.RootElement.TryGetProperty("detail", out var text))
            {
                detail = text.GetString();
            }
        }
        catch (JsonException)
        {
            // Not a problem document; the status code is all there is to say.
        }

        throw new ApiException(detail ?? $"The server answered {(int)response.StatusCode}.");
    }
}

public sealed class ApiException(string message) : Exception(message);
