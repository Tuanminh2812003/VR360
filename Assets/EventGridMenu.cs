using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

public class EventGridMenu : MonoBehaviour
{
    [Header("Where event JSONs are")]
    [Tooltip("Bỏ trống = Application.persistentDataPath")]
    public string folderOverride = "";
    [Tooltip("Chỉ lấy những file khớp pattern này")]
    public string searchPattern = "*.json";
    [Tooltip("Bỏ qua những file có tên này (vd: file tổng)")]
    public string[] excludeFileNames = { "vr360_list.json" };

    [Header("Logo base URL")]
    [Tooltip("Nếu eventInfo.logo là relative, sẽ ghép với baseUrl này")]
    public string baseUrl = "http://45.124.94.12:8080/";

    [Header("UI – Scroll/Grid")]
    public ScrollRect scrollRect;
    public RectTransform content;       // Content của ScrollView
    public GridLayoutGroup grid;        // Gắn trên Content
    public int columns = 3;
    public Vector2 cellSize = new Vector2(260, 240);
    public Vector2 spacing = new Vector2(24, 24);

    [Header("Item Prefab")]
    public VRGridMenuItem itemPrefab;   // dùng lại prefab item thumbnail+title+button
    public Sprite fallbackThumb;

    [Header("Behaviour")]
    public bool buildOnEnable = true;

    [Header("Events")]
    [Tooltip("Bắn ra đường dẫn file .json đã chọn (full path)")]
    public UnityEvent<string> onEventSelected;

    // ==== JSON models (khớp file bạn lưu) ====
    [Serializable]
    public class EventInfo
    {
        public string _id;
        public string title;
        public string intro;
        public string logo;       // relative hoặc absolute
        public string streaming;
        public string username;
        public string password;
        public string createdAt;
        public string updatedAt;
        public int __v;
        public System.Collections.Generic.List<string> video_list;
    }
    [Serializable]
    public class EventBundle
    {
        public EventInfo eventInfo;      // bắt buộc có
        public VRItemList videos;        // có thể rỗng
    }
    [Serializable] public class VRItem { public string _id; public string title; public string path; public string thumbnail; public string url; public string thumbUrl; }
    [Serializable] public class VRItemList { public System.Collections.Generic.List<VRItem> items; }

    readonly List<GameObject> spawned = new();

    void OnEnable()
    {
        if (buildOnEnable) Rebuild();
    }

    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        // clear cũ
        foreach (var go in spawned) if (go) Destroy(go);
        spawned.Clear();

        // layout
        if (grid)
        {
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columns);
        }

        string dir = string.IsNullOrEmpty(folderOverride) ? Application.persistentDataPath : folderOverride;
        if (!Directory.Exists(dir))
        {
            Debug.LogWarning("[EventGrid] Folder not found: " + dir);
            return;
        }

        var files = SafeGetFiles(dir, searchPattern);
        if (files == null || files.Length == 0)
        {
            Debug.Log("[EventGrid] No json files.");
            return;
        }

        // loại các file exclude
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (excludeFileNames != null) foreach (var n in excludeFileNames) if (!string.IsNullOrEmpty(n)) exclude.Add(n);

        string thumbCacheDir = Path.Combine(Application.persistentDataPath, "thumbs");
        Directory.CreateDirectory(thumbCacheDir);

        int count = 0;
        foreach (var f in files)
        {
            var name = Path.GetFileName(f);
            if (exclude.Contains(name)) continue;

            // thử parse (ít nhất phải có eventInfo)
            if (!TryReadEventBundle(f, out var bundle)) continue;
            if (bundle.eventInfo == null) continue;

            string displayTitle = Path.GetFileNameWithoutExtension(f); // yêu cầu: title = tên file json
            string logoPath = bundle.eventInfo.logo ?? string.Empty;
            string logoUrl = ToAbsoluteUrl(baseUrl, logoPath);

            // spawn 1 item
            var go = Instantiate(itemPrefab.gameObject, content);
            var item = go.GetComponent<VRGridMenuItem>();
            spawned.Add(go);

            // click callback
            void OnClick()
            {
                // Lưu eventId vào config và xoá ép stream cũ
                AppConfigManager.SetEventId(bundle.eventInfo._id);
                AppConfigManager.SetStreamingVideoId("");

                // (tuỳ bạn) phát intro rồi mở panel danh sách video:
                var flow = FindObjectOfType<IntroEventFlow>(true);
                if (flow)
                {
                    flow.PlayIntroThenOpenList(f); // f = full path tới file 1234.json
                }
                else
                {
                    onEventSelected?.Invoke(f);
                }
            }

            item.Bind(displayTitle, logoUrl, thumbCacheDir, OnClick, fallbackThumb, this);
            count++;
        }

        // tính chiều cao content cho scroll
        if (content && grid)
        {
            int rows = Mathf.CeilToInt(count / (float)columns);
            float h = rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y + grid.padding.top + grid.padding.bottom;
            var size = content.sizeDelta; size.y = h; content.sizeDelta = size;
        }

        if (scrollRect) scrollRect.verticalNormalizedPosition = 1f;
    }

    // === Helpers ===

    static string[] SafeGetFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern); }
        catch { return Array.Empty<string>(); }
    }

    bool TryReadEventBundle(string path, out EventBundle bundle)
    {
        bundle = null;
        try
        {
            var json = File.ReadAllText(path);
            var b = JsonUtility.FromJson<EventBundle>(json);
            if (b != null && b.eventInfo != null)
            {
                bundle = b;
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[EventGrid] Parse fail: " + e.Message);
        }
        return false;
    }

    static string ToAbsoluteUrl(string baseUrl, string maybeRelative)
    {
        if (string.IsNullOrEmpty(maybeRelative)) return string.Empty;
        // đã absolute
        if (maybeRelative.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            maybeRelative.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return maybeRelative;

        if (string.IsNullOrEmpty(baseUrl)) return maybeRelative;
        if (!baseUrl.EndsWith("/")) baseUrl += "/";
        return baseUrl + maybeRelative.TrimStart('/');
    }
}
