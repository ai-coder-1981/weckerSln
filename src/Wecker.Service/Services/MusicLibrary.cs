namespace Wecker.Service.Services;

public class MusicLibrary(DataPaths paths)
{
    public IReadOnlyList<string> GetFiles() =>
        Directory.Exists(paths.Music)
            ? Directory.EnumerateFiles(paths.Music, "*.mp3").Select(f => Path.GetFileName(f)).Order().ToList()
            : [];

    /// <summary>Voller Pfad oder null. Verhindert Path-Traversal (nur reine Dateinamen).</summary>
    public string? GetPath(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name)) return null;
        var path = Path.Combine(paths.Music, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Speichert einen Upload. Gibt (Dateiname, null) oder (null, Fehlermeldung) zurück.</summary>
    public async Task<(string? Name, string? Error)> SaveAsync(string originalName, Stream content)
    {
        var name = Sanitize(originalName);
        if (name is null || !name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            return (null, "Nur MP3-Dateien sind erlaubt.");

        var target = Path.Combine(paths.Music, name);
        if (File.Exists(target)) return (null, $"'{name}' existiert bereits.");

        var tmp = target + ".tmp";
        try
        {
            await using (var fs = File.Create(tmp))
                await content.CopyToAsync(fs);

            if (!LooksLikeMp3(tmp))
            {
                File.Delete(tmp);
                return (null, "Die Datei ist keine gültige MP3.");
            }
            File.Move(tmp, target);
            return (name, null);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* ignorieren */ }
            throw;
        }
    }

    private static string? Sanitize(string fileName)
    {
        var name = Path.GetFileName(fileName ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length == 0 || name.StartsWith('.') || name.Length > 120) return null;
        return name;
    }

    private static bool LooksLikeMp3(string path)
    {
        Span<byte> b = stackalloc byte[3];
        using var fs = File.OpenRead(path);
        if (fs.Read(b) < 3) return false;
        var id3 = b[0] == 'I' && b[1] == 'D' && b[2] == '3';
        var frameSync = b[0] == 0xFF && (b[1] & 0xE0) == 0xE0;
        return id3 || frameSync;
    }
}
