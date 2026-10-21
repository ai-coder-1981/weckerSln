namespace Wecker.Service.Services;

public record DataPaths(string Root)
{
    public string Music => Path.Combine(Root, "music");
    public string AlarmsFile => Path.Combine(Root, "alarms.json");
}
