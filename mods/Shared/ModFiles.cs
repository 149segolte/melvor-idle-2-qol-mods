using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace MelvorMods.Shared;

/// <summary>Files in a mod's own folder: written whole, and read without ever losing what was there.</summary>
public static class ModFiles
{
    /// <summary>Writes beside the file and swaps it in, so a crash half way never leaves a broken file.</summary>
    public static void WriteWhole(string file, string text)
    {
        string draft = file + ".tmp";
        File.WriteAllText(draft, text);
        File.Move(draft, file, overwrite: true);
    }

    /// <summary>
    /// Reads a settings file. No file yet gives the defaults. A file that cannot be understood is set aside
    /// as "name.broken" (so the next save does not wipe what the player had) and the defaults are used.
    /// </summary>
    public static T ReadJson<T>(string file, Action<string> log) where T : new()
    {
        if (!File.Exists(file)) return new T();
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(file)) ?? new T(); }
        catch (Exception e)
        {
            string name = Path.GetFileName(file);
            try { File.Move(file, file + ".broken", overwrite: true); } catch { }
            log($"{name} could not be read ({e.Message}). It was set aside as {name}.broken; the defaults are used.");
            return new T();
        }
    }

    public static void WriteJson<T>(string file, T value, Action<string> log)
    {
        try { WriteWhole(file, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { log($"Could not save {Path.GetFileName(file)}: {e.Message}"); }
    }
}

/// <summary>
/// "Is it time again?", by the clock and not by counting frames: a frame rate is not a tempo, and a mod that
/// looks at the scene "every 30 frames" does it seventeen times a second on a fast machine.
/// </summary>
public sealed class Tempo(ulong everyMs)
{
    ulong _last;

    public bool Due()
    {
        ulong now = Time.GetTicksMsec();
        if (_last != 0 && now - _last < everyMs) return false;
        _last = now;
        return true;
    }
}
