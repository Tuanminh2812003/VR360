using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Video;

public class IntroEventFlow : MonoBehaviour
{
    [Header("Panels & UI")]
    [Tooltip("Panel toàn màn hình chứa RawImage + VideoPlayer để phát intro")]
    public GameObject introPanel;          // PanelIntro
    [Tooltip("VideoPlayer gắn trên RawImage trong IntroPanel")]
    public VideoPlayer introPlayer;        // VideoPlayer trên IntroRaw
    [Tooltip("Panel chứa ScrollView danh sách sự kiện (màn chọn sự kiện)")]
    public GameObject eventListPanel;      // Panel (chế độ sự kiện)
    [Tooltip("Panel chứa danh sách video của sự kiện")]
    public GameObject videoListPanel;      // Panel (panel danh sách VR360)
    [Tooltip("Text tiêu đề ở panel video list (thay cho logo)")]
    public TMP_Text headerTitleText;       // Text để thay logo bằng title

    [Header("Video List Loader")]
    [Tooltip("VRGridMenu đang render danh sách video (đọc từ JSON trong persistentDataPath)")]
    public VRGridMenu gridMenu;            // set trong inspector

    [Header("Download")]
    [Tooltip("Base URL để ghép intro/streaming tương đối (nếu JSON không cho absolute URL)")]
    public string baseUrl = "http://45.124.94.12:8080/";
    [Tooltip("Timeout mỗi request (giây)")]
    public int httpTimeoutSec = 25;

    [Header("Skip")]
    public Button skipButton;              // optional – để người dùng bỏ qua intro

    // ==== JSON models (khớp với file username.json đã lưu) ====
    [Serializable]
    public class EventInfo
    {
        public string _id;
        public string title;
        public string intro;       // đường dẫn intro (relative) hoặc để trống
        public string streaming;   // nếu backend dùng key này cho intro
        public string logo;
        public List<string> video_list;
        public string username;
        public string password;
        public string createdAt;
        public string updatedAt;
        public int __v;
    }

    [Serializable]
    public class VRItem
    {
        public string _id;
        public string title;
        public string description;
        public string type;
        public long size;
        public string path;
        public string thumbnail;
        public string url;
        public string thumbUrl;
        public string createdAt;
        public string updatedAt;
    }
    [Serializable] public class VRItemList { public List<VRItem> items = new List<VRItem>(); }

    [Serializable]
    public class EventBundle
    {
        public EventInfo eventInfo;
        public VRItemList videos;
    }

    // =================== PUBLIC ENTRY ===================

    /// <summary>
    /// Gọi từ EventGridMenu khi click 1 item sự kiện.
    /// Truyền vào đường dẫn tuyệt đối của file JSON (persistentDataPath/1234.json).
    /// </summary>
    public void PlayIntroThenOpenList(string eventJsonAbsPath)
    {
        StartCoroutine(Flow_Co(eventJsonAbsPath));
    }

    // =================== FLOW ===========================

    IEnumerator Flow_Co(string jsonAbsPath)
    {
        // 0) Đọc JSON
        if (string.IsNullOrEmpty(jsonAbsPath) || !File.Exists(jsonAbsPath))
        {
            Debug.LogWarning("[IntroFlow] JSON path invalid: " + jsonAbsPath);
            yield break;
        }

        EventBundle bundle = null;
        try
        {
            var raw = File.ReadAllText(jsonAbsPath, Encoding.UTF8);
            bundle = JsonUtility.FromJson<EventBundle>(raw);
        }
        catch (Exception e)
        {
            Debug.LogError("[IntroFlow] Parse JSON failed: " + e.Message);
            yield break;
        }

        if (bundle == null || bundle.eventInfo == null)
        {
            Debug.LogWarning("[IntroFlow] Bundle/eventInfo null.");
            yield break;
        }

        // 1) Cập nhật header title (thay logo)
        if (headerTitleText) headerTitleText.text = bundle.eventInfo.title ?? string.Empty;

        // 2) Intro path
        string id = bundle.eventInfo._id ?? "event";
        string localIntro = Path.Combine(Application.persistentDataPath, id + "_intro.mp4");

        // 3) Nếu intro chưa có → thử tải
        if (!File.Exists(localIntro))
        {
            // Ưu tiên field "intro", sau đó "streaming"
            string introField = !string.IsNullOrEmpty(bundle.eventInfo.intro)
                                ? bundle.eventInfo.intro
                                : bundle.eventInfo.streaming;
            if (!string.IsNullOrEmpty(introField))
            {
                string url = CombineUrlMaybe(baseUrl, introField);
                yield return StartCoroutine(DownloadBinary(url, localIntro));
            }
        }

        // 4) Phát intro nếu có file, nếu không có thì bỏ qua
        if (File.Exists(localIntro))
        {
            yield return StartCoroutine(PlayIntroFile(localIntro));
        }

        // 5) Chuyển sang panel danh sách video + rebuild grid từ file json này
        if (eventListPanel) eventListPanel.SetActive(false);
        if (videoListPanel) videoListPanel.SetActive(true);

        if (gridMenu != null)
        {
            gridMenu.jsonFileName = Path.GetFileName(jsonAbsPath); // gridMenu đọc từ persistentDataPath
            gridMenu.Build();
        }
    }

    // =================== INTRO PLAYBACK =================

    IEnumerator PlayIntroFile(string absPath)
    {
        // Bật intro panel, ẩn panel video/event nếu cần
        if (eventListPanel) eventListPanel.SetActive(false);
        if (videoListPanel) videoListPanel.SetActive(false);
        if (introPanel) introPanel.SetActive(true);

        if (skipButton)
        {
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(() => StopIntroNow());
        }

        bool finished = false;

        if (!introPlayer)
        {
            Debug.LogWarning("[IntroFlow] introPlayer null → skip.");
            yield break;
        }

        introPlayer.source = VideoSource.Url;
        // Đảm bảo path chuyển thành file://
        string p = absPath.Replace("\\", "/");
        introPlayer.url = "file://" + (p.StartsWith("/") ? "" : "/") + Uri.EscapeUriString(p);

        introPlayer.loopPointReached -= OnIntroFinished;
        introPlayer.loopPointReached += OnIntroFinished;

        void MarkFinished(VideoPlayer _)
        {
            finished = true;
        }

        introPlayer.errorReceived -= OnIntroError;
        introPlayer.errorReceived += OnIntroError;

        introPlayer.Play();

        // Chờ kết thúc/skip
        while (!finished && introPlayer.isPlaying)
            yield return null;

        // Tắt intro panel
        if (introPanel) introPanel.SetActive(false);

        // Gỡ handler
        introPlayer.loopPointReached -= OnIntroFinished;
        introPlayer.errorReceived -= OnIntroError;

        // local handlers
        void OnIntroFinished(VideoPlayer vp) => MarkFinished(vp);
        void OnIntroError(VideoPlayer vp, string msg)
        {
            Debug.LogWarning("[IntroFlow] Intro error: " + msg);
            MarkFinished(vp);
        }
    }

    void StopIntroNow()
    {
        if (introPlayer && introPlayer.isPlaying) introPlayer.Stop();
        if (introPanel) introPanel.SetActive(false);
    }

    // =================== DOWNLOAD HELPERS ===============

    IEnumerator DownloadBinary(string url, string saveAbsPath)
    {
        if (string.IsNullOrEmpty(url)) yield break;

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = Mathf.Max(5, httpTimeoutSec);
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                Debug.LogWarning($"[IntroFlow] Download intro failed: {url} -> {req.error}");
                yield break;
            }

            try
            {
                var dir = Path.GetDirectoryName(saveAbsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllBytes(saveAbsPath, req.downloadHandler.data);
                Debug.Log("[IntroFlow] Intro saved: " + saveAbsPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IntroFlow] Save intro failed: " + e.Message);
            }
        }
    }

    // =================== UTILS ==========================

    static string CombineUrlMaybe(string baseUrl, string maybeRelative)
    {
        // Nếu đã là absolute (http/https/file) -> giữ nguyên
        if (maybeRelative.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            maybeRelative.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            maybeRelative.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return maybeRelative;

        if (string.IsNullOrEmpty(baseUrl)) return maybeRelative;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return baseUrl + maybeRelative.TrimStart('/');
    }
}
