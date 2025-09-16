// Assets/VRGridMenu.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VRGridMenu : MonoBehaviour
{
    [Header("JSON")]
    [Tooltip("Tên file JSON nằm trong Application.persistentDataPath")]
    public string jsonFileName = "vr360_list.json";

    [Header("UI – Scroll/Grid")]
    public ScrollRect scrollRect;
    public RectTransform content;
    public GridLayoutGroup grid;
    public int columns = 3;
    public int rowsVisible = 2;
    public Vector2 cellSize = new Vector2(480, 260);
    public Vector2 spacing = new Vector2(24, 24);

    [Header("Item Prefab")]
    public VRGridMenuItem itemPrefab;
    public Sprite fallbackThumb;

    [Header("Behavior")]
    public bool buildOnEnable = true;

    [Header("Player (optional)")]
    public Skybox360Player player;

    [Header("VOD")]
    public VRVideoOnDemand vod;

    // ===== JSON Models =====
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
    [Serializable]
    public class VRItemList { public List<VRItem> items = new List<VRItem>(); }

    readonly List<GameObject> spawned = new List<GameObject>();

    // Cache danh sách hiện tại để tra cứu nhanh theo _id
    private VRItemList _currentListCache;

    void OnEnable()
    {
        if (buildOnEnable) Build();
    }

    [ContextMenu("Rebuild Menu")]
    public void Build()
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

        // đọc JSON
        string path = Path.Combine(Application.persistentDataPath, jsonFileName);
        if (!File.Exists(path))
        {
            Debug.LogWarning("[VRGridMenu] JSON not found: " + path);
            _currentListCache = null;
            return;
        }

        VRItemList list = null;
        try
        {
            var json = File.ReadAllText(path);
            list = JsonUtility.FromJson<VRItemList>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("[VRGridMenu] Parse error: " + e.Message);
            _currentListCache = null;
            return;
        }

        if (list == null || list.items == null || list.items.Count == 0)
        {
            Debug.LogWarning("[VRGridMenu] No items in JSON.");
            _currentListCache = null;
            return;
        }

        // LƯU CACHE để EventStreamingWatcher tra cứu
        _currentListCache = list;

        // cache dir cho thumbnail
        string thumbCacheDir = Path.Combine(Application.persistentDataPath, "thumbs");
        Directory.CreateDirectory(thumbCacheDir);

        // spawn UI items
        foreach (var it in list.items)
        {
            var go = Instantiate(itemPrefab.gameObject, content);
            var item = go.GetComponent<VRGridMenuItem>();
            spawned.Add(go);

            void OnClick()
            {
                if (player != null && vod != null)
                {
                    // phát theo id: nếu đã có <id>.mp4 thì mở local, chưa có thì stream + tải nền
                    vod.Play(player, it._id, it.url);
                }
            }

            item.Bind(it.title, it.thumbUrl, thumbCacheDir, OnClick, fallbackThumb, this);
        }

        // tính chiều cao content cho scroll
        if (content && grid)
        {
            int count = list.items.Count;
            int rows = Mathf.CeilToInt(count / (float)columns);
            float h = rows * grid.cellSize.y
                      + Mathf.Max(0, rows - 1) * grid.spacing.y
                      + grid.padding.top + grid.padding.bottom;
            var size = content.sizeDelta;
            size.y = h;
            content.sizeDelta = size;
        }

        // đưa scroll về đầu
        if (scrollRect) scrollRect.verticalNormalizedPosition = 1f;
    }

    // ===== Public helpers =====

    /// <summary>
    /// Tra URL theo _id dựa trên danh sách hiện đang load.
    /// </summary>
    public string GetUrlById(string id)
    {
        if (string.IsNullOrEmpty(id) || _currentListCache == null || _currentListCache.items == null)
            return null;

        for (int i = 0; i < _currentListCache.items.Count; i++)
        {
            var it = _currentListCache.items[i];
            if (it != null && string.Equals(it._id, id, StringComparison.Ordinal))
                return string.IsNullOrEmpty(it.url) ? null : it.url;
        }
        return null;
    }

    /// <summary>
    /// Khoá/mở khoá tương tác của grid (dùng khi server ép phát video qua 'streaming').
    /// </summary>
    public void SetInteractable(bool enable)
    {
        var cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = enable;
        cg.blocksRaycasts = enable;
        // Không đổi alpha để UI không bị mờ: cg.alpha = 1f;
    }

    /// <summary>
    /// Rebuild lại menu (alias cho Build).
    /// </summary>
    public void Rebuild() => Build();

    /// <summary>
    /// Đổi file JSON rồi build lại ngay.
    /// </summary>
    public void SetJsonAndRebuild(string newJsonFileName)
    {
        if (!string.IsNullOrEmpty(newJsonFileName))
            jsonFileName = newJsonFileName;
        Build();
    }

    // ===== Nếu cần stream trước & tải nền (không còn dùng khi đã có VOD.Play) =====
    async Task HandleClickAsync(string url)
    {
        if (player == null) return;

        string local = DownloadManager.UrlToLocalPath(url);
        DownloadManager.CleanupStalePart(local);

        bool hasLocal = !string.IsNullOrEmpty(local) &&
                        File.Exists(local) &&
                        new FileInfo(local).Length > 1024;

        try
        {
            if (hasLocal)
            {
                await player.PlayAbsolutePathAsync(local);
            }
            else
            {
                await player.PlayUrlAsync(url);            // stream ngay
                _ = DownloadManager.EnsureDownloaded(url); // tải nền
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[VRGridMenu] Play failed: " + e.Message);
        }
    }

}
