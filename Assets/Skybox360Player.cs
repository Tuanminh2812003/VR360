using UnityEngine;
using UnityEngine.Video;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class Skybox360Player : MonoBehaviour
{
    [Header("References")]
    public VideoPlayer vp;
    public AudioSource audioSrc;
    public RenderTexture videoRT;
    public Material skyboxMat;

    [Header("Idle Skybox (HDRI)")]
    public Material hdriSkyboxMat;
    public bool useHDRIWhenIdle = true;
    public float hdriRotation = 0f;
    public float reflectionIntensity = 1f;

    [Header("Options")]
    public string fileName = "video1-out.mp4";
    public bool loop = false;
    public float startRotation = 0f;

    [Header("Idle Fill (chỉ khi không dùng HDRI)")]
    public Color idleColor = Color.white;
    public bool clearOnAwake = true;

    [NonSerialized] public string[] videoPaths = Array.Empty<string>();
    [NonSerialized] public int currentIndex = -1;
    static readonly string[] kExt = { ".mp4", ".mov", ".m4v", ".mkv", ".webm" };

    public enum PlaylistSortMode { NaturalByName, LastWriteTimeDesc }
    public PlaylistSortMode sortMode = PlaylistSortMode.NaturalByName;

    bool isSwitching = false;
    float lastStartTS = -999f;
    float lastSwitchTS = -999f;
    const float ARM_DELAY = 0.30f;
    const float SWITCH_DEBOUNCE = 0.20f;

    static readonly Regex kFirstNumber = new Regex(@"\d+", RegexOptions.Compiled);
    static (bool hasNum, long num, string lowerName) NaturalKey(string filePath)
    {
        string name = Path.GetFileName(filePath);
        var m = kFirstNumber.Match(name);
        if (m.Success && long.TryParse(m.Value, out long n))
            return (true, n, name.ToLowerInvariant());
        return (false, long.MaxValue, name.ToLowerInvariant());
    }

    Material _videoSkyboxMat;

    void Awake()
    {
        if (vp)
        {
            vp.playOnAwake = false;
            vp.source = VideoSource.Url;
            vp.clip = null;

            vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
            if (audioSrc)
            {
                vp.EnableAudioTrack(0, true);
                vp.SetTargetAudioSource(0, audioSrc);
            }

            vp.isLooping = loop;
            vp.waitForFirstFrame = true;
            vp.skipOnDrop = false;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.targetTexture = videoRT;

            vp.errorReceived += (player, msg) => Debug.LogError("[VideoPlayer] " + msg);
            vp.loopPointReached -= OnLoopReached;
            vp.loopPointReached += OnLoopReached;
        }

        _videoSkyboxMat = skyboxMat;

        if (skyboxMat && videoRT)
        {
            skyboxMat.SetTexture("_MainTex", videoRT);
            skyboxMat.SetFloat("_Rotation", startRotation);
        }

        if (useHDRIWhenIdle && hdriSkyboxMat) UseHdrSky();
        else if (skyboxMat) RenderSettings.skybox = skyboxMat;

        if (clearOnAwake) SetIdleSky();
    }

    void Start() => RefreshList();

    void UseVideoSky()
    {
        if (_videoSkyboxMat)
        {
            RenderSettings.skybox = _videoSkyboxMat;
            DynamicGI.UpdateEnvironment();
        }
    }

    void UseHdrSky()
    {
        if (hdriSkyboxMat)
        {
            if (hdriSkyboxMat.HasProperty("_Rotation"))
                hdriSkyboxMat.SetFloat("_Rotation", hdriRotation);

            RenderSettings.skybox = hdriSkyboxMat;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            DynamicGI.UpdateEnvironment();
        }
    }

    public void SetIdleSky()
    {
        if (vp)
        {
            if (vp.isPlaying || vp.isPrepared) vp.Stop();
        }

        if (useHDRIWhenIdle && hdriSkyboxMat)
        {
            UseHdrSky();
        }
        else
        {
            if (videoRT)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = videoRT;
                GL.Clear(true, true, idleColor);
                RenderTexture.active = prev;
            }
            if (_videoSkyboxMat) RenderSettings.skybox = _videoSkyboxMat;
        }
    }

    public void StopPlaybackToIdle()
    {
        SetIdleSky();
        currentIndex = -1;
        isSwitching = false;
    }

    void OnLoopReached(VideoPlayer source)
    {
        if (loop) return;
        if (Time.unscaledTime - lastStartTS < ARM_DELAY) return;
        if (isSwitching) return;
        SetIdleSky();
    }

    public void PlayNext()
    {
        if (isSwitching) return;
        if (Time.unscaledTime - lastSwitchTS < SWITCH_DEBOUNCE) return;
        NextInternal("[OnClick]");
    }

    public void PlayByIndex(int index)
    {
        if (videoPaths == null || videoPaths.Length == 0) return;
        index = Mathf.Clamp(index, 0, videoPaths.Length - 1);
        currentIndex = index;
        _ = PlayAbsolutePathAsync(videoPaths[currentIndex]);
    }

    public void RefreshList()
    {
        try
        {
            string root = Application.persistentDataPath;
            if (!Directory.Exists(root))
            {
                videoPaths = Array.Empty<string>();
                currentIndex = -1;
                return;
            }

            var files = Directory.GetFiles(root)
                                 .Where(p => kExt.Contains(Path.GetExtension(p).ToLowerInvariant()));

            switch (sortMode)
            {
                case PlaylistSortMode.LastWriteTimeDesc:
                    videoPaths = files
                        .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
                        .ThenBy(p => Path.GetFileName(p).ToLowerInvariant())
                        .ToArray();
                    break;

                default:
                    videoPaths = files
                        .OrderBy(p => NaturalKey(p).hasNum ? 0 : 1)
                        .ThenBy(p => NaturalKey(p).num)
                        .ThenBy(p => NaturalKey(p).lowerName)
                        .ToArray();
                    break;
            }

            currentIndex = (videoPaths.Length > 0) ? 0 : -1;

            Debug.Log("[VideoPlayer] Playlist order:");
            for (int i = 0; i < videoPaths.Length; i++)
                Debug.Log($"  [{i}] {Path.GetFileName(videoPaths[i])}");
        }
        catch (Exception e)
        {
            Debug.LogWarning("RefreshList error: " + e.Message);
            videoPaths = Array.Empty<string>();
            currentIndex = -1;
        }
    }

    public void PlayFromPersistentDataPath(string file)
    {
        string path = Path.Combine(Application.persistentDataPath, file);
        _ = PlayAbsolutePathAsync(path);
    }

    // PHÁT FILE LOCAL (absolute path)
    public async Task PlayAbsolutePathAsync(string fullPath)
    {
        if (!vp) return;
        if (isSwitching) return;

        isSwitching = true;
        lastSwitchTS = Time.unscaledTime;

        if (vp.isPlaying || vp.isPrepared) vp.Stop();

        if (!useHDRIWhenIdle && videoRT)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = videoRT;
            GL.Clear(true, true, idleColor);
            RenderTexture.active = prev;
        }

        string p = fullPath.Replace("\\", "/");
        string url = "file://" + (p.StartsWith("/") ? "" : "/") + Uri.EscapeUriString(p);

        int idx = Array.IndexOf(videoPaths, fullPath);
        if (idx >= 0) currentIndex = idx;

        await PrepareAndPlay(url);
        isSwitching = false;
    }

    // PHÁT TRỰC TIẾP TỪ INTERNET (URL)
    public async Task PlayUrlAsync(string url)
    {
        if (!vp) return;
        if (isSwitching) return;

        isSwitching = true;
        lastSwitchTS = Time.unscaledTime;

        if (vp.isPlaying || vp.isPrepared) vp.Stop();

        if (!useHDRIWhenIdle && videoRT)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = videoRT;
            GL.Clear(true, true, idleColor);
            RenderTexture.active = prev;
        }

        await PrepareAndPlay(url);
        isSwitching = false;
    }

    async Task PrepareAndPlay(string finalUrl)
    {
        vp.url = finalUrl;
        vp.isLooping = loop;

        UseVideoSky();

        vp.Prepare();
        float t0 = Time.realtimeSinceStartup;
        while (!vp.isPrepared)
        {
            await Task.Yield();
            if (Time.realtimeSinceStartup - t0 > 10f)
            {
                Debug.LogWarning("[VideoPlayer] Prepare timeout");
                break;
            }
        }
        vp.Play();
        lastStartTS = Time.unscaledTime;
    }

    void NextInternal(string reason)
    {
        if (videoPaths == null || videoPaths.Length == 0) return;
        int n = videoPaths.Length;
        int next = currentIndex < 0 ? 0 : (currentIndex + 1) % n;
        currentIndex = next;
        Debug.Log($"[VideoPlayer] Next {reason}: {Path.GetFileName(videoPaths[currentIndex])}");
        _ = PlayAbsolutePathAsync(videoPaths[currentIndex]);
    }
}
