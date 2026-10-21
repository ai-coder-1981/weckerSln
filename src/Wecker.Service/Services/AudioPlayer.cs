using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace Wecker.Service.Services;

public class PlayerOptions
{
    public string Command { get; set; } = "mpg123";
    public string[] Args { get; set; } = [];       // Platzhalter {file}
    public string? Device { get; set; }            // optional, nur mpg123: -a <Device>
    public int MaxMinutes { get; set; } = 10;      // Sicherheits-Stopp
}

/// <summary>Spielt MP3 über einen externen Player (mpg123) in Endlosschleife.</summary>
public sealed class AudioPlayer(IOptions<PlayerOptions> options, MusicLibrary music, ILogger<AudioPlayer> log)
{
    private readonly PlayerOptions _opt = options.Value;
    private readonly object _lock = new();
    private Process? _proc;
    private CancellationTokenSource? _timeout;

    public string? CurrentFile { get; private set; }
    public bool IsPlaying { get { lock (_lock) return _proc is not null; } }

    /// <summary>Wird ausgelöst, wenn die Wiedergabe endet (Stopp, Timeout oder Player beendet).</summary>
    public event Action? Stopped;

    public void Play(string file)
    {
        StopCore(raise: false);

        var path = music.GetPath(file) ?? throw new FileNotFoundException("Musikdatei nicht gefunden", file);

        var psi = new ProcessStartInfo(_opt.Command) { UseShellExecute = false, CreateNoWindow = true };
        if (!string.IsNullOrWhiteSpace(_opt.Device))
        {
            psi.ArgumentList.Add("-a");
            psi.ArgumentList.Add(_opt.Device);
        }
        var args = _opt.Args.Length > 0 ? _opt.Args : ["-q", "--loop", "-1", "{file}"];
        foreach (var a in args) psi.ArgumentList.Add(a.Replace("{file}", path));

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.Exited += (_, _) => OnExited(p);

        try { p.Start(); }
        catch (Exception ex)
        {
            log.LogError(ex, "Player '{Cmd}' konnte nicht gestartet werden (ist mpg123 installiert?)", _opt.Command);
            throw;
        }

        var cts = new CancellationTokenSource();
        lock (_lock) { _proc = p; CurrentFile = file; _timeout = cts; }

        _ = Task.Delay(TimeSpan.FromMinutes(_opt.MaxMinutes), cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) { log.LogInformation("Maximale Klingeldauer erreicht"); Stop(); }
        });
    }

    public void Stop() => StopCore(raise: true);

    private void StopCore(bool raise)
    {
        Process? p;
        lock (_lock)
        {
            p = _proc; _proc = null; CurrentFile = null;
            _timeout?.Cancel(); _timeout = null;
        }
        if (p is null) return;
        try { if (!p.HasExited) p.Kill(true); } catch (Exception ex) { log.LogWarning(ex, "Player konnte nicht beendet werden"); }
        if (raise) Stopped?.Invoke();
    }

    private void OnExited(Process p)
    {
        bool current;
        lock (_lock)
        {
            current = ReferenceEquals(_proc, p);
            if (current) { _proc = null; CurrentFile = null; _timeout?.Cancel(); _timeout = null; }
        }
        if (current)
        {
            log.LogWarning("Player wurde unerwartet beendet (Exit-Code {Code})", p.ExitCode);
            Stopped?.Invoke();
        }
    }
}
