using System.Net.Http.Json;
using Wecker.Contracts;

namespace Wecker.Web.Services;

/// <summary>Einziger Zugang des Frontends zur Logik: HTTP-Client auf Wecker.Service.</summary>
public class AlarmApiClient(HttpClient http)
{
    // Kurzer Timeout für normale Aufrufe; Uploads dürfen länger dauern.
    private static CancellationToken Short() => new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;

    public async Task<List<AlarmDto>> GetAlarmsAsync() =>
        await http.GetFromJsonAsync<List<AlarmDto>>("api/alarms", Short()) ?? [];

    public async Task<List<string>> GetMusicAsync() =>
        await http.GetFromJsonAsync<List<string>>("api/music", Short()) ?? [];

    public async Task<StatusDto?> GetStatusAsync() =>
        await http.GetFromJsonAsync<StatusDto>("api/status", Short());

    public async Task<SaveResultDto> SaveAsync(AlarmDto alarm)
    {
        var response = await http.PutAsJsonAsync($"api/alarms/{alarm.Id}", alarm, Short());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SaveResultDto>())!;
    }

    /// <summary>Gibt eine Fehlermeldung zurück oder null.</summary>
    public async Task<string?> TestAsync(int id)
    {
        var response = await http.PostAsync($"api/alarms/{id}/test", null, Short());
        return await ErrorOrNull(response);
    }

    public async Task StopAsync() =>
        (await http.PostAsync("api/stop", null, Short())).EnsureSuccessStatusCode();

    /// <summary>Lädt eine MP3 hoch. Fehlermeldung oder null.</summary>
    public async Task<string?> UploadAsync(string fileName, Stream data)
    {
        using var content = new MultipartFormDataContent();
        var file = new StreamContent(data);
        file.Headers.ContentType = new("audio/mpeg");
        content.Add(file, "file", fileName);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var response = await http.PostAsync("api/music", content, cts.Token);
        return await ErrorOrNull(response);
    }

    public async Task<string?> DeleteMusicAsync(string fileName)
    {
        var response = await http.DeleteAsync($"api/music/{Uri.EscapeDataString(fileName)}", Short());
        return await ErrorOrNull(response);
    }

    public async Task<ShutdownDto> GetShutdownAsync() =>
        await http.GetFromJsonAsync<ShutdownDto>("api/shutdown", Short()) ?? new ShutdownDto();

    public async Task<SaveShutdownResultDto> SaveShutdownAsync(ShutdownDto dto)
    {
        var response = await http.PutAsJsonAsync("api/shutdown", dto, Short());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SaveShutdownResultDto>())!;
    }

    public async Task ShutdownNowAsync() =>
        (await http.PostAsync("api/shutdown/now", null, Short())).EnsureSuccessStatusCode();

    private static async Task<string?> ErrorOrNull(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return null;
        try { return await response.Content.ReadFromJsonAsync<string>() ?? "Fehler"; }
        catch { return $"Fehler ({(int)response.StatusCode})"; }
    }
}
