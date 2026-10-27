using System.Text.Json;
using Godot;

/// <summary>
/// Single owner of user://settings.json: language + audio volumes.
/// Loc reads/writes the lang field through here so the two never clobber
/// each other's keys. Music/SFX buses are created on demand and volumes
/// are applied straight to AudioServer.
/// </summary>
public static class Settings
{
    public static string Lang = "zh-CN";
    public static float Master = 0.8f;
    public static float Music = 0.8f;
    public static float Sfx = 0.8f;

    private static bool _loaded;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // nullable so an old file that only stores lang keeps volume defaults
    private class Data
    {
        public string lang { get; set; }
        public float? vol_master { get; set; }
        public float? vol_music { get; set; }
        public float? vol_sfx { get; set; }
    }

    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        var f = FileAccess.Open("user://settings.json", FileAccess.ModeFlags.Read);
        if (f != null)
        {
            try
            {
                var d = JsonSerializer.Deserialize<Data>(f.GetAsText());
                if (d != null)
                {
                    if (!string.IsNullOrEmpty(d.lang)) Lang = d.lang;
                    Master = Mathf.Clamp(d.vol_master ?? Master, 0f, 1f);
                    Music = Mathf.Clamp(d.vol_music ?? Music, 0f, 1f);
                    Sfx = Mathf.Clamp(d.vol_sfx ?? Sfx, 0f, 1f);
                }
            }
            catch (System.Exception e)
            {
                GD.PushWarning($"[Settings] read fail: {e.Message}");
            }
            f.Close();
        }
        ApplyAudio();
    }

    public static void Save()
    {
        var f = FileAccess.Open("user://settings.json", FileAccess.ModeFlags.Write);
        if (f == null) return;
        f.StoreString(JsonSerializer.Serialize(new Data
        {
            lang = Lang,
            vol_master = Master,
            vol_music = Music,
            vol_sfx = Sfx,
        }, JsonOpts));
        f.Close();
    }

    public static void ApplyAudio()
    {
        EnsureBuses();
        SetBus("Master", Master);
        SetBus("Music", Music);
        SetBus("SFX", Sfx);
    }

    private static void EnsureBuses()
    {
        foreach (var name in new[] { "Music", "SFX" })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int idx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(idx, name);
            AudioServer.SetBusSend(idx, "Master");
        }
    }

    private static void SetBus(string name, float linear)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusMute(idx, linear <= 0.001f);
        if (linear > 0.001f)
            AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(linear));
    }
}
