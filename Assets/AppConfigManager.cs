using System;
using System.IO;
using UnityEngine;

[Serializable]
public class AppConfig
{
    public string currentEventId = "";
    public string streamingVideoId = "";
    public long updatedAtUnix = 0; // chỉ để debug/log
}

public static class AppConfigManager
{
    static readonly object _lock = new object();
    static string _pathCache;
    static AppConfig _cache;

    public static string ConfigPath
    {
        get
        {
            if (string.IsNullOrEmpty(_pathCache))
                _pathCache = Path.Combine(Application.persistentDataPath, "config.json");
            return _pathCache;
        }
    }

    /// <summary>Tạo file nếu chưa có, load config vào cache.</summary>
    public static AppConfig Load()
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    _cache = new AppConfig();
                    _cache.updatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    Save(_cache);
                }
                else
                {
                    var json = File.ReadAllText(ConfigPath);
                    _cache = string.IsNullOrWhiteSpace(json)
                        ? new AppConfig()
                        : JsonUtility.FromJson<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AppConfig] Load fail: " + e.Message);
                _cache = new AppConfig();
            }

            return _cache;
        }
    }

    public static void Save(AppConfig cfg)
    {
        lock (_lock)
        {
            try
            {
                cfg.updatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var json = JsonUtility.ToJson(cfg, true);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AppConfig] Save fail: " + e.Message);
            }
            _cache = cfg;
        }
    }

    public static void ResetFields()
    {
        lock (_lock)
        {
            var cfg = Load();
            cfg.currentEventId = "";
            cfg.streamingVideoId = "";
            Save(cfg);
            Debug.Log("[AppConfig] Reset currentEventId & streamingVideoId");
        }
    }

    public static void SetEventId(string eventId)
    {
        lock (_lock)
        {
            var cfg = Load();
            cfg.currentEventId = eventId ?? "";
            Save(cfg);
        }
    }

    public static void SetStreamingVideoId(string videoId)
    {
        lock (_lock)
        {
            var cfg = Load();
            cfg.streamingVideoId = videoId ?? "";
            Save(cfg);
        }
    }

    public static string GetEventId() => Load().currentEventId;
    public static string GetStreamingVideo() => Load().streamingVideoId;
}
