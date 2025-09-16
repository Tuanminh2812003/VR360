// Assets/ApiVR360Fetcher.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class ApiVR360Fetcher : MonoBehaviour
{
    [Header("API")]
    [Tooltip("Endpoint trả danh sách media")]
    public string apiUrl = "http://45.124.94.12:8080/api/v1/mediafile?page=1&size=50";
    [Tooltip("Base URL để ghép với path video (vd: http://host/)")]
    public string baseUrl = "http://45.124.94.12:8080/";
    [Tooltip("Base URL để ghép với thumbnail. Bỏ trống = dùng baseUrl")]
    public string thumbBaseUrl = "";

    [Header("Output JSON")]
    public string outputFileName = "vr360_list.json";
    public bool prettyPrint = true;

    [Header("Download (optional)")]
    public bool downloadFiles = false;
    public string downloadSubFolder = "vr360";
    public int maxDownloadsPerRun = 0;          // 0 = không giới hạn

    [Header("Hook Skybox Player (optional)")]
    public Skybox360Player player;

    [Header("Auto/Timeout")]
    public bool fetchOnStart = true;
    public float httpTimeoutSec = 20f;

    // ===== Models =====
    [Serializable]
    public class VRItem
    {
        public string _id;
        public string title;
        public string description;
        public string type;
        public long size;

        // backend fields
        public string path;         // video path (relative)
        public string thumbnail;    // new field (relative)

        // computed absolute urls
        public string url;          // absolute video url
        public string thumbUrl;     // absolute thumbnail url

        public string createdAt;
        public string updatedAt;
    }
    [Serializable] public class VRItemList { public List<VRItem> items = new List<VRItem>(); }

    // Envelopes phổ biến
    [Serializable] class WrapItems { public List<VRItem> items; }
    [Serializable] class WrapData { public List<VRItem> data; }
    [Serializable] class WrapResult { public List<VRItem> result; }
    [Serializable] class WrapRecords { public List<VRItem> records; }
    [Serializable] class WrapRows { public List<VRItem> rows; }
    [Serializable] class WrapList { public List<VRItem> list; }

    void Start()
    {
        if (fetchOnStart) StartCoroutine(FetchAndSaveAll());
    }

    [ContextMenu("Fetch Now")]
    public void FetchNow()
    {
        StartCoroutine(FetchAndSaveAll());
    }

    [ContextMenu("Open Save Folder")]
    void CtxOpenSaveFolder()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(Application.persistentDataPath);
#else
        Debug.Log("[VR360] Save folder: " + Application.persistentDataPath);
#endif
    }

    IEnumerator FetchAndSaveAll()
    {
        Debug.Log("[VR360] Fetch: " + apiUrl);

        using (var req = UnityWebRequest.Get(apiUrl))
        {
            req.timeout = Mathf.CeilToInt(httpTimeoutSec);
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                Debug.LogError($"[VR360] API error ({req.responseCode}): {req.error}");
                yield break;
            }

            string raw = req.downloadHandler.text ?? "";
            string head = raw.Length > 300 ? raw.Substring(0, 300) : raw;
            Debug.Log($"[VR360] API OK ({req.responseCode}), bytes={raw.Length}, head={head.Replace("\n", " ")}");

            // Parse linh hoạt
            var all = ParseFlexibleAll(raw);
            if (all == null || all.items == null || all.items.Count == 0)
            {
                Debug.LogWarning("[VR360] No items parsed from API.");
                yield break;
            }

            // Lọc vr360 + video/*
            var filtered = new VRItemList();
            string thumbBase = string.IsNullOrEmpty(thumbBaseUrl) ? baseUrl : thumbBaseUrl;

            foreach (var it in all.items)
            {
                if (it == null) continue;
                var p = it.path ?? "";
                var t = (it.type ?? "").ToLowerInvariant();
                bool isVr360 = p.IndexOf("vr360", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isVideo = string.IsNullOrEmpty(t) || t.StartsWith("video/");

                if (isVr360 && isVideo)
                {
                    it.url = CombineUrl(baseUrl, it.path);
                    it.thumbUrl = CombineUrl(thumbBase, it.thumbnail);
                    filtered.items.Add(it);
                }
            }

            if (filtered.items.Count == 0)
            {
                Debug.LogWarning("[VR360] Filter result empty (no vr360).");
                yield break;
            }

            // Save JSON
            string jsonOut = prettyPrint ? JsonUtility.ToJson(filtered, true)
                                         : JsonUtility.ToJson(filtered, false);
            string outPath = Path.Combine(Application.persistentDataPath, outputFileName);
            try
            {
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(outPath, jsonOut, Encoding.UTF8);
                Debug.Log($"[VR360] Saved list ({filtered.items.Count}) → {outPath}");
            }
            catch (Exception e)
            {
                Debug.LogError("[VR360] Write file failed: " + e.Message);
            }

            // Download (tuỳ chọn)
            if (downloadFiles)
                yield return StartCoroutine(DownloadAll(filtered));
        }

        if (player) player.RefreshList();
    }

    // ================== Parse linh hoạt ==================

    VRItemList ParseFlexibleAll(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        string trimmed = raw.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');

        // TH1: mảng thuần [...]
        if (trimmed.StartsWith("["))
        {
            var list = ParseArrayToList(trimmed);
            if (list != null && list.items.Count > 0) return list;
        }

        // TH2: object bọc
        if (trimmed.StartsWith("{"))
        {
            // 2.1) Thử parse thẳng vào các envelope quen thuộc
            try
            {
                var wi = JsonUtility.FromJson<WrapItems>(trimmed); if (wi != null && wi.items != null && wi.items.Count > 0) return new VRItemList { items = wi.items };
                var wd = JsonUtility.FromJson<WrapData>(trimmed); if (wd != null && wd.data != null && wd.data.Count > 0) return new VRItemList { items = wd.data };
                var wr = JsonUtility.FromJson<WrapResult>(trimmed); if (wr != null && wr.result != null && wr.result.Count > 0) return new VRItemList { items = wr.result };
                var wrc = JsonUtility.FromJson<WrapRecords>(trimmed); if (wrc != null && wrc.records != null && wrc.records.Count > 0) return new VRItemList { items = wrc.records };
                var wro = JsonUtility.FromJson<WrapRows>(trimmed); if (wro != null && wro.rows != null && wro.rows.Count > 0) return new VRItemList { items = wro.rows };
                var wl = JsonUtility.FromJson<WrapList>(trimmed); if (wl != null && wl.list != null && wl.list.Count > 0) return new VRItemList { items = wl.list };
            }
            catch { /* rơi xuống các bước sau */ }

            // 2.2) Rút mảng theo key bất kỳ phổ biến
            string[] keys = { "items", "data", "result", "records", "rows", "list" };
            foreach (var k in keys)
            {
                if (TryExtractArrayByKey(trimmed, k, out var arrJson))
                {
                    var list = ParseArrayToList(arrJson);
                    if (list != null && list.items.Count > 0) return list;
                }
            }
        }

        // TH3: fallback Regex – gom hết object có "path"
        var viaRegex = ParseByRegex(trimmed);
        if (viaRegex != null && viaRegex.items.Count > 0) return viaRegex;

        return new VRItemList(); // rỗng
    }

    VRItemList ParseArrayToList(string arrayJson)
    {
        try { return JsonUtility.FromJson<VRItemList>("{\"items\":" + arrayJson + "}"); }
        catch { return null; }
    }

    // Rút một mảng theo key bằng đọc ký tự (không dùng regex cho ngoặc lồng)
    bool TryExtractArrayByKey(string json, string key, out string arrayJson)
    {
        arrayJson = null;
        var low = json.ToLowerInvariant();
        string needle = "\"" + key.ToLowerInvariant() + "\"";
        int i = low.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return false;

        // tìm dấu '[' sau dấu ':'
        int colon = low.IndexOf(':', i);
        if (colon < 0) return false;
        int j = colon + 1;
        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
        if (j >= json.Length || json[j] != '[') return false;

        int start = j;
        int depth = 0;
        for (; j < json.Length; j++)
        {
            char c = json[j];
            if (c == '[') depth++;
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    arrayJson = json.Substring(start, j - start + 1);
                    return true;
                }
            }
        }
        return false;
    }

    // Fallback: pull tất cả object có "path" và (nếu có) "thumbnail"
    VRItemList ParseByRegex(string json)
    {
        var list = new VRItemList();
        var objMatches = Regex.Matches(json, "\\{[^{}]*\"path\"\\s*:\\s*\"[^\"]+\"[^{}]*\\}");
        foreach (Match m in objMatches)
        {
            string obj = m.Value;
            var item = new VRItem
            {
                _id = ExtractString(obj, "\"_id\"\\s*:\\s*\"([^\"]+)\""),
                title = ExtractString(obj, "\"title\"\\s*:\\s*\"([^\"]*)\""),
                description = ExtractString(obj, "\"description\"\\s*:\\s*\"([^\"]*)\""),
                type = ExtractString(obj, "\"type\"\\s*:\\s*\"([^\"]*)\""),
                path = ExtractString(obj, "\"path\"\\s*:\\s*\"([^\"]+)\""),
                thumbnail = ExtractString(obj, "\"thumbnail\"\\s*:\\s*\"([^\"]+)\""),
                createdAt = ExtractString(obj, "\"createdAt\"\\s*:\\s*\"([^\"]+)\""),
                updatedAt = ExtractString(obj, "\"updatedAt\"\\s*:\\s*\"([^\"]+)\""),
                size = ExtractLong(obj, "\"size\"\\s*:\\s*(\\d+)")
            };
            if (!string.IsNullOrEmpty(item.path))
                list.items.Add(item);
        }
        return list;
    }

    static string ExtractString(string src, string pattern)
    {
        var m = Regex.Match(src, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }
    static long ExtractLong(string src, string pattern)
    {
        var m = Regex.Match(src, pattern);
        return (m.Success && long.TryParse(m.Groups[1].Value, out var v)) ? v : 0L;
    }

    // ================== Download ==================

    IEnumerator DownloadAll(VRItemList list)
    {
        string root = Path.Combine(Application.persistentDataPath, downloadSubFolder);
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);

        int limit = (maxDownloadsPerRun <= 0) ? int.MaxValue : maxDownloadsPerRun;
        int downloaded = 0;

        foreach (var it in list.items)
        {
            if (downloaded >= limit) break;
            if (it == null || string.IsNullOrEmpty(it.url)) continue;

            string fileName = Path.GetFileName((it.path ?? "").Replace("\\", "/"));
            if (string.IsNullOrEmpty(fileName)) fileName = (it._id ?? Guid.NewGuid().ToString()) + ".mp4";

            string local = Path.Combine(root, fileName);
            if (File.Exists(local)) continue; // skip nếu đã có

            using (var req = UnityWebRequest.Get(it.url))
            {
                req.timeout = Mathf.CeilToInt(httpTimeoutSec);
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok)
                {
                    Debug.LogWarning($"[VR360] Download failed: {it.url} → {req.error}");
                    continue;
                }

                try
                {
                    File.WriteAllBytes(local, req.downloadHandler.data);
                    downloaded++;
                    Debug.Log($"[VR360] Downloaded: {fileName}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[VR360] Save failed ({fileName}): {e.Message}");
                }
            }
        }

        Debug.Log($"[VR360] Download finished. New files: {downloaded}");
    }

    // ================== Utils ==================

    static string CombineUrl(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b ?? "";
        if (string.IsNullOrEmpty(b)) return a ?? "";
        if (!a.EndsWith("/")) a += "/";
        b = b.Replace("\\", "/").TrimStart('/');
        return a + b;
    }

}
