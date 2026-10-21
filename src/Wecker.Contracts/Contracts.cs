namespace Wecker.Contracts;

public enum AlarmMode { Once, Daily, Weekdays }

public record AlarmDto
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public TimeSpan? Time { get; set; } = new TimeSpan(7, 0, 0);
    public AlarmMode Mode { get; set; } = AlarmMode.Daily;
    public DateTime? Date { get; set; }
    public string? File { get; set; }
}

/// <summary>Antwort auf PUT /api/alarms/{id}. Error != null: Wecker wurde gespeichert, aber deaktiviert.</summary>
public record SaveResultDto(AlarmDto Alarm, string? Error);

public record StatusDto(int? RingingAlarmId, string? RingingFile, int? NextAlarmId, DateTimeOffset? NextFireTime,
                        DateTimeOffset? NextShutdownTime = null);

public record ShutdownDto
{
    public bool Enabled { get; set; }
    public TimeSpan? Time { get; set; } = new TimeSpan(22, 0, 0);
    public AlarmMode Mode { get; set; } = AlarmMode.Daily;
    public DateTime? Date { get; set; }
}

/// <summary>Antwort auf PUT /api/shutdown. Error != null: Zeitplan wurde gespeichert, aber deaktiviert.</summary>
public record SaveShutdownResultDto(ShutdownDto Shutdown, string? Error);

public static class ApiRoutes
{
    public const string StatusHub = "/hub/status";
    public const string StatusChanged = "StatusChanged";
    public const string MusicChanged = "MusicChanged";
}
