using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Quartz;
using Wecker.Contracts;
using Wecker.Service.Hubs;
using Wecker.Service.Jobs;

namespace Wecker.Service.Services;

public class AlarmService
{
    private const int AlarmCount = 3;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly ISchedulerFactory _factory;
    private readonly DataPaths _paths;
    private readonly MusicLibrary _music;
    private readonly AudioPlayer _player;
    private readonly IHubContext<StatusHub> _hub;
    private readonly ILogger<AlarmService> _log;
    private readonly object _fileLock = new();

    private List<AlarmDto> _alarms = [];
    private int? _ringingId;

    public AlarmService(ISchedulerFactory factory, DataPaths paths, MusicLibrary music, AudioPlayer player,
                        IHubContext<StatusHub> hub, ILogger<AlarmService> log, ShutdownService shutdown)
    {
        _factory = factory; _paths = paths; _music = music; _player = player; _hub = hub; _log = log;
        _player.Stopped += () => { _ringingId = null; _ = BroadcastAsync(); };
        shutdown.Changed += () => _ = BroadcastAsync();
    }

    public IReadOnlyList<AlarmDto> GetAlarms() => _alarms.Select(a => a with { }).ToList();

    public async Task InitializeAsync()
    {
        Load();
        foreach (var a in _alarms)
            if (await ScheduleAsync(a) is not null) a.Enabled = false;
        Save();
    }

    public async Task<SaveResultDto?> SaveAsync(int id, AlarmDto dto)
    {
        var alarm = _alarms.FirstOrDefault(a => a.Id == id);
        if (alarm is null) return null;

        alarm.Enabled = dto.Enabled;
        alarm.Time = dto.Time;
        alarm.Mode = dto.Mode;
        alarm.Date = dto.Date;
        alarm.File = dto.File;

        var error = await ScheduleAsync(alarm);
        if (error is not null) alarm.Enabled = false;
        Save();
        await BroadcastAsync();
        return new SaveResultDto(alarm with { }, error);
    }

    /// <summary>Wird vom Quartz-Job aufgerufen.</summary>
    public async Task FireAsync(int id)
    {
        var alarm = _alarms.FirstOrDefault(a => a.Id == id);
        if (alarm is null) return;
        _log.LogInformation("Wecker {Id} klingelt ({File})", id, alarm.File);

        if (alarm.Mode == AlarmMode.Once) { alarm.Enabled = false; Save(); }
        TryPlay(alarm);
        await BroadcastAsync();
    }

    /// <summary>Probehören der Datei eines Weckers. Gibt Fehlermeldung oder null zurück.</summary>
    public async Task<string?> TestAsync(int id)
    {
        var alarm = _alarms.FirstOrDefault(a => a.Id == id);
        if (alarm is null) return "Wecker nicht gefunden.";
        if (_music.GetPath(alarm.File) is null) return "Bitte zuerst eine Musikdatei wählen.";
        var ok = TryPlay(alarm);
        await BroadcastAsync();
        return ok ? null : "Player konnte nicht gestartet werden (siehe Service-Log).";
    }

    public async Task StopAsync()
    {
        _player.Stop();
        _ringingId = null;
        await BroadcastAsync();
    }

    public async Task<StatusDto> GetStatusAsync()
    {
        var scheduler = await _factory.GetScheduler();
        int? nextId = null;
        DateTimeOffset? next = null;
        foreach (var a in _alarms.Where(a => a.Enabled))
        {
            var trigger = await scheduler.GetTrigger(new TriggerKey($"alarm-{a.Id}-trigger"));
            var fire = trigger?.GetNextFireTimeUtc();
            if (fire is not null && (next is null || fire < next)) { next = fire; nextId = a.Id; }
        }
        var shutdownTrigger = await scheduler.GetTrigger(new TriggerKey(ShutdownService.TriggerName));
        var nextShutdown = shutdownTrigger?.GetNextFireTimeUtc()?.ToLocalTime();
        var playing = _player.IsPlaying;
        return new StatusDto(playing ? _ringingId : null, playing ? _player.CurrentFile : null,
                             nextId, next?.ToLocalTime(), nextShutdown);
    }

    public async Task NotifyMusicChangedAsync()
    {
        try { await _hub.Clients.All.SendAsync(ApiRoutes.MusicChanged); }
        catch (Exception ex) { _log.LogWarning(ex, "Musik-Broadcast fehlgeschlagen"); }
    }

    /// <summary>Löscht eine MP3. Verweigert, wenn ein Wecker die Datei verwendet. Fehlermeldung oder null.</summary>
    public async Task<string?> DeleteMusicAsync(string file)
    {
        var path = _music.GetPath(file);
        if (path is null) return "Datei nicht gefunden.";

        var user = _alarms.FirstOrDefault(a => a.File == file);
        if (user is not null) return $"Die Datei wird von Wecker {user.Id} verwendet. Erst dort eine andere Datei wählen.";

        if (_player.CurrentFile == file) await StopAsync();
        File.Delete(path);
        await NotifyMusicChangedAsync();
        return null;
    }

    private bool TryPlay(AlarmDto alarm)
    {
        try
        {
            _player.Play(alarm.File!);
            _ringingId = alarm.Id;
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Wiedergabe für Wecker {Id} fehlgeschlagen", alarm.Id);
            return false;
        }
    }

    private async Task BroadcastAsync()
    {
        try
        {
            var status = await GetStatusAsync();
            await _hub.Clients.All.SendAsync(ApiRoutes.StatusChanged, status);
        }
        catch (Exception ex) { _log.LogWarning(ex, "Status-Broadcast fehlgeschlagen"); }
    }

    private async Task<string?> ScheduleAsync(AlarmDto a)
    {
        var scheduler = await _factory.GetScheduler();
        var jobKey = new JobKey($"alarm-{a.Id}");
        await scheduler.DeleteJob(jobKey); // löscht auch die Trigger

        if (!a.Enabled) return null;
        if (a.Time is null) return "Bitte eine Uhrzeit wählen.";
        if (_music.GetPath(a.File) is null) return "Bitte eine vorhandene Musikdatei wählen.";

        var t = a.Time.Value;
        var job = JobBuilder.Create<AlarmJob>().WithIdentity(jobKey).UsingJobData("id", a.Id).Build();
        var tb = TriggerBuilder.Create().WithIdentity($"alarm-{a.Id}-trigger");
        ITrigger trigger;

        switch (a.Mode)
        {
            case AlarmMode.Once:
                if (a.Date is null) return "Bitte ein Datum wählen.";
                var start = a.Date.Value.Date + t;
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

    private void Load()
    {
        try
        {
            if (File.Exists(_paths.AlarmsFile))
                _alarms = JsonSerializer.Deserialize<List<AlarmDto>>(File.ReadAllText(_paths.AlarmsFile)) ?? [];
        }
        catch (Exception ex) { _log.LogWarning(ex, "alarms.json konnte nicht gelesen werden"); }

        for (var i = 1; i <= AlarmCount; i++)
            if (_alarms.All(a => a.Id != i)) _alarms.Add(new AlarmDto { Id = i });
        _alarms = _alarms.Where(a => a.Id is >= 1 and <= AlarmCount).OrderBy(a => a.Id).ToList();
    }

    private void Save()
    {
        lock (_fileLock)
            File.WriteAllText(_paths.AlarmsFile, JsonSerializer.Serialize(_alarms, Json));
    }
}
