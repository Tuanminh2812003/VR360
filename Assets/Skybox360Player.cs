// Assets/Skybox360Player.cs
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
    [Tooltip("VideoPlayer dùng để phát 360 equirectangular vào RenderTexture")]
    public VideoPlayer vp;
    public AudioSource audioSrc;
    [Tooltip("RT mà Skybox Video sẽ đọc làm _MainTex")]
    public RenderTexture videoRT;
    [Tooltip("Material Skybox/Panoramic cho VIDEO (gán _MainTex = videoRT)")]
    public Material skyboxMat;

    [Header("Idle Skybox (HDRI)")]
    [Tooltip("Material Skybox/Panoramic cho HDRI (ảnh .hdr/.exr)")]
    public Material hdriSkyboxMat;
    [Tooltip("Bật dùng HDRI khi không phát video")]
    public bool useHDRIWhenIdle = true;
    [Tooltip("Xoay HDRI khi idle (độ)")]
    public float hdriRotation = 0f;
    [Tooltip("Cường độ phản xạ môi trường")]
    public float reflectionIntensity = 1f;

    [Header("Options")]
    [Tooltip("Tên file fallback nếu muốn play 1 file mặc định (trong persistentDataPath)")]
    public string fileName = "video1-out.mp4";
    [Tooltip("Loop 1 file (không auto-next)")]
    public bool loop = false;
    [Tooltip("Góc xoay ban đầu của skybox video (độ)")]
    public float startRotation = 0f;

    [Header("Idle Fill (chỉ khi không dùng HDRI)")]
    [Tooltip("Nếu không dùng HDRI, có thể tô RT một màu khi idle")]
    public Color idleColor = Color.white;
    public bool clearOnAwake = true;

    // ===== Playlist =====
    [NonSerialized] public string[] videoPaths = Array.Empty<string>();
    [NonSerialized] public int currentIndex = -1;
    static readonly string[] kExt = { ".mp4", ".mov", ".m4v", ".mkv", ".webm" };

    public enum PlaylistSortMode { NaturalByName, LastWriteTimeDesc }
    public PlaylistSortMode sortMode = PlaylistSortMode.NaturalByName;

    // ----- State / Guards -----
    bool   isSwitching = false;
    float  lastStartTS = -999f;
    float  lastSwitchTS = -999f;
    const float ARM_DELAY = 0.30f;       // 0.3s đầu bỏ qua event thừa
    const float SWITCH_DEBOUNCE = 0.20f; // debounce click Next

    // Natural sort helpers
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

    // =======================================================================
    // Lifecycle
    // =======================================================================
    void Awake()
    {
        // Chuẩn bị VideoPlayer → RenderTexture
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

        // Lưu lại material video
        _videoSkyboxMat = skyboxMat;

        // Gán RT + rotation cho material video (nếu có)
        if (skyboxMat && videoRT)
        {
            skyboxMat.SetTexture("_MainTex", videoRT);
            skyboxMat.SetFloat("_Rotation", startRotation);
        }

        // Lúc khởi động: idle = HDRI (nếu bật), ngược lại vẫn để skybox video (nhưng RT sẽ bị tô idleColor)
        if (useHDRIWhenIdle && hdriSkyboxMat) UseHdrSky();
        else if (skyboxMat) RenderSettings.skybox = skyboxMat;

        // Tô RT để tránh frame cũ khi không dùng HDRI
        if (clearOnAwake) SetIdleSky();
    }

    void Start()
    {
        // Không auto-play. Chỉ quét danh sách để UI dùng.
        RefreshList();
    }

    // =======================================================================
    // Skybox Switchers
    // =======================================================================
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

    // =======================================================================
    // Public Idle Controls
    // =======================================================================
    /// <summary>
    /// Dừng playback và chuyển về HDRI (nếu bật) hoặc tô RT
    /// </summary>
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
            // Tô RT một màu để skybox video là nền trơn
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

    // =======================================================================
    // Events
    // =======================================================================
    void OnLoopReached(VideoPlayer source)
    {
        if (loop) return;
        if (Time.unscaledTime - lastStartTS < ARM_DELAY) return;
        if (isSwitching) return;

        // Tới cuối → về HDRI (idle)
        SetIdleSky();
        // Nếu muốn auto-next thì thay bằng: NextInternal("[loopPointReached]");
    }

    // =======================================================================
    // Playlist API
    // =======================================================================
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

    /// <summary>
    /// Load & phát video từ đường dẫn tuyệt đối (file path)
    /// </summary>
    public async Task PlayAbsolutePathAsync(string fullPath)
    {
        if (!vp) return;
        if (isSwitching) return;

        isSwitching = true;
        lastSwitchTS = Time.unscaledTime;

        // Dừng clip cũ
        if (vp.isPlaying || vp.isPrepared) vp.Stop();

        // Clear RT để không lưu frame cũ (nếu vẫn dùng skybox video khi idle)
        if (!useHDRIWhenIdle && videoRT)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = videoRT;
            GL.Clear(true, true, idleColor);
            RenderTexture.active = prev;
        }

        // Chuẩn hoá URL "file://"
        string p = fullPath.Replace("\\", "/");
        string url = "file://" + (p.StartsWith("/") ? "" : "/") + Uri.EscapeUriString(p);

        // Đồng bộ index nếu path thuộc playlist
        int idx = Array.IndexOf(videoPaths, fullPath);
        if (idx >= 0) currentIndex = idx;

        // Gán URL rồi Prepare
        vp.url = url;
        vp.isLooping = loop;

        // Chuyển skybox về VIDEO trước khi Play
        UseVideoSky();

        vp.Prepare();
        // chờ prepare
        float t0 = Time.realtimeSinceStartup;
        while (!vp.isPrepared)
        {
            await Task.Yield();
            // timeout thô sơ (10s)
            if (Time.realtimeSinceStartup - t0 > 10f)
            {
                Debug.LogWarning("[VideoPlayer] Prepare timeout");
                break;
            }
        }

        // Play
        vp.Play();
        lastStartTS = Time.unscaledTime;

        isSwitching = false;
    }

    // =======================================================================
    // Helpers
    // =======================================================================
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
