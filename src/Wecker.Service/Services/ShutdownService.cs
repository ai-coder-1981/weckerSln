using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Quartz;
using Wecker.Contracts;
using Wecker.Service.Jobs;

namespace Wecker.Service.Services;

public class ShutdownOptions
{
    /// <summary>Befehl zum Herunterfahren (Leerzeichen trennen die Argumente).</summary>
    public string Command { get; set; } = "sudo -n /usr/bin/systemctl poweroff";
}

public class ShutdownService(ISchedulerFactory factory, DataPaths paths, AudioPlayer player,
                             IOptions<ShutdownOptions> options, ILogger<ShutdownService> log)
{
    public const string JobName = "shutdown";
    public const string TriggerName = "shutdown-trigger";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private string FilePath => Path.Combine(paths.Root, "shutdown.json");

    public ShutdownDto Current { get; private set; } = new();

    /// <summary>Zeitplan hat sich geändert (für Status-Broadcast).</summary>
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<ShutdownDto>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) { log.LogWarning(ex, "shutdown.json konnte nicht gelesen werden"); }

        if (await ScheduleAsync(Current) is not null) Current.Enabled = false;
        Save();
    }

    public async Task<SaveShutdownResultDto> SaveAsync(ShutdownDto dto)
    {
        Current = dto with { };
        var error = await ScheduleAsync(Current);
        if (error is not null) Current.Enabled = false;
        Save();
        Changed?.Invoke();
        return new SaveShutdownResultDto(Current with { }, error);
    }

    /// <summary>Wird vom Quartz-Job aufgerufen.</summary>
    public void ExecuteScheduled()
    {
        log.LogInformation("Geplantes Herunterfahren");
        if (Current.Mode == AlarmMode.Once) { Current.Enabled = false; Save(); }
        RunShutdown();
    }

    /// <summary>Fährt mit kurzer Verzögerung herunter, damit die HTTP-Antwort noch zurückgeht.</summary>
    public void ShutdownSoon()
    {
        log.LogInformation("Herunterfahren per UI angefordert");
        _ = Task.Run(async () => { await Task.Delay(1500); RunShutdown(); });
    }

    private void RunShutdown()
    {
        player.Stop();

        if (!OperatingSystem.IsLinux())
        {
            log.LogWarning("Kein Linux: Herunterfahren wird nur simuliert.");
            return;
        }

        try
        {
            var parts = options.Value.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var psi = new ProcessStartInfo(parts[0]) { UseShellExecute = false };
            foreach (var a in parts.Skip(1)) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Herunterfahren fehlgeschlagen (sudoers-Eintrag vorhanden?)");
        }
    }

    private async Task<string?> ScheduleAsync(ShutdownDto s)
    {
        var scheduler = await factory.GetScheduler();
        var jobKey = new JobKey(JobName);
        await scheduler.DeleteJob(jobKey);

        if (!s.Enabled) return null;
        if (s.Time is null) return "Bitte eine Uhrzeit wählen.";

        var t = s.Time.Value;
        var job = JobBuilder.Create<ShutdownJob>().WithIdentity(jobKey).Build();
        var tb = TriggerBuilder.Create().WithIdentity(TriggerName);
        ITrigger trigger;

        switch (s.Mode)
        {
            case AlarmMode.Once:
                if (s.Date is null) return "Bitte ein Datum wählen.";
                var start = s.Date.Value.Date + t;
                if (start <= DateTime.Now) return "Der Zeitpunkt liegt in der Vergangenheit.";
                trigger = tb.StartAt(new DateTimeOffset(start)).Build();
                break;
            case AlarmMode.Daily:
                trigger = tb.WithCronSchedule($"0 {t.Minutes} {t.Hours} ? * *",
                    x => x.WithMisfireHandlingInstructionDoNothing()).Build();
                break;
            default:
                trigger = tb.WithCronSchedule($"0 {t.Minutes} {t.Hours} ? * MON-FRI",
                    x => x.WithMisfireHandlingInstructionDoNothing()).Build();
                break;
        }

        await scheduler.ScheduleJob(job, trigger);
        return null;
    }

    private void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, Json));
}
