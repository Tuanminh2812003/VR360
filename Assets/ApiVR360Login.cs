using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class ApiVR360Login : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField usernameField;
    public TMP_InputField passwordField;

    [Header("API")]
    public string eventApiUrl = "http://45.124.94.12:8080/api/v1/event?size=50";
    public string mediaApiUrl = "http://45.124.94.12:8080/api/v1/mediafile?page=1&size=50";
    public string baseUrl = "http://45.124.94.12:8080/";   // cho url video
    public string thumbBaseUrl = "";                       // trống = dùng baseUrl

    [Header("Output")]
    public bool prettyPrint = true;

    // =================== MODELS ===================

    [Serializable]
    public class EventItem
    {
        public string _id;
        public string title;
        public string intro;         // thêm
        public string logo;          // thêm
        public List<string> video_list;
        public string streaming;     // thêm
        public string username;
        public string password;
        public string createdAt;     // thêm
        public string updatedAt;     // thêm
        public int __v;              // thêm
    }
    [Serializable] class EventEnvelope { public List<EventItem> data; }
    [Serializable] class WrapEvents { public List<EventItem> items; }

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

        // computed:
        public string url;
        public string thumbUrl;

        public string createdAt;
        public string updatedAt;
    }
    [Serializable] public class VRItemList { public List<VRItem> items = new List<VRItem>(); }
    [Serializable] class VRItemEnvelope { public List<VRItem> data; }

    // Gói output cuối cùng: eventInfo + items (KHÔNG còn "videos")
    [Serializable]
    public class LoginBundle
    {
        public EventItem eventInfo;          // toàn bộ info event
        public List<VRItem> items = new();   // danh sách video ở root
    }

    // =================== UI HOOK ===================

    public void OnClickLogin()
    {
        var user = usernameField ? (usernameField.text ?? "").Trim() : "";
        var pass = passwordField ? (passwordField.text ?? "").Trim() : "";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            Debug.LogWarning("[Login] Username/Password trống.");
            return;
        }

        Debug.Log($"[Login] Trying {user}/{pass}");
        StartCoroutine(TryLogin(user, pass));
    }

    // =================== FLOW ===================

    IEnumerator TryLogin(string user, string pass)
    {
        // 1) Gọi /event, lấy danh sách event trong "data"
        List<EventItem> events = null;

        using (var req = UnityWebRequest.Get(eventApiUrl))
        {
            req.timeout = 20;
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                Debug.LogError("[Login] Event API error: " + req.error);
                yield break;
            }

            string raw = req.downloadHandler.text ?? "";
            events = ParseEvents(raw) ?? new List<EventItem>();

            if (events.Count == 0)
            {
                Debug.LogWarning("[Login] No events found.");
                yield break;
            }
        }

        // 2) Tìm event có user/pass khớp
        EventItem match = events.Find(e =>
            (e.username ?? "").Trim().Equals(user, StringComparison.Ordinal) &&
            (e.password ?? "").Trim().Equals(pass, StringComparison.Ordinal));

        if (match == null)
        {
            Debug.LogWarning("[Login] Sai username/password.");
            yield break;
        }

        // 3) Gọi /mediafile → lọc theo id trong video_list → build list items
        var filtered = new VRItemList();

        if (match.video_list != null && match.video_list.Count > 0)
        {
            using (var req = UnityWebRequest.Get(mediaApiUrl))
            {
                req.timeout = 25;
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok)
                {
                    Debug.LogError("[Login] Media API error: " + req.error);
                    yield break;
                }

                string raw = req.downloadHandler.text ?? "";
                var all = ParseMedia(raw) ?? new VRItemList();

                // lọc theo video_list
                string tBase = string.IsNullOrEmpty(thumbBaseUrl) ? baseUrl : thumbBaseUrl;
                var set = new HashSet<string>(match.video_list);

                foreach (var it in all.items)
                {
                    if (it == null) continue;
                    if (set.Contains(it._id))
                    {
                        it.url = CombineUrl(baseUrl, it.path);
                        it.thumbUrl = CombineUrl(tBase, it.thumbnail);
                        filtered.items.Add(it);
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("[Login] Event hợp lệ nhưng video_list rỗng.");
        }

        // 4) Gói dữ liệu & Ghi file username.json (eventInfo + items)
        var bundle = new LoginBundle
        {
            eventInfo = match,
            items = filtered.items           // <-- lưu thẳng items
        };

        string outPath = Path.Combine(Application.persistentDataPath, user + ".json");
        try
        {
            string jsonOut = prettyPrint ? JsonUtility.ToJson(bundle, true)
                                         : JsonUtility.ToJson(bundle, false);
            File.WriteAllText(outPath, jsonOut, Encoding.UTF8);
            Debug.Log($"[Login] Saved → {outPath} (items={bundle.items.Count})");
        }
        catch (Exception e)
        {
            Debug.LogError("[Login] Save failed: " + e.Message);
        }
    }

    // =================== PARSERS ===================

    List<EventItem> ParseEvents(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return new List<EventItem>();

        // TH1: {"data":[...]}
        try
        {
            var env = JsonUtility.FromJson<EventEnvelope>(raw);
            if (env != null && env.data != null && env.data.Count > 0) return env.data;
        }
        catch { /* ignore */ }

        // TH2: rút "data":[...] nếu bọc khác
        if (TryExtractArrayByKey(raw, "data", out var arr))
        {
            try
            {
                var wrap = JsonUtility.FromJson<WrapEvents>("{\"items\":" + arr + "}");
                if (wrap != null && wrap.items != null) return wrap.items;
            }
            catch { }
        }

        // TH3: mảng thuần [...]
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith("["))
        {
            try { return JsonUtility.FromJson<WrapEvents>("{\"items\":" + trimmed + "}").items; }
            catch { }
        }

        return new List<EventItem>();
    }

    VRItemList ParseMedia(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return new VRItemList();

        // TH1: {"data":[...]}
        try
        {
            var env = JsonUtility.FromJson<VRItemEnvelope>(raw);
            if (env != null && env.data != null && env.data.Count > 0)
                return new VRItemList { items = env.data };
        }
        catch { /* ignore */ }

        // TH2: rút "data":[...]
        if (TryExtractArrayByKey(raw, "data", out var arr))
        {
            try
            {
                return JsonUtility.FromJson<VRItemList>("{\"items\":" + arr + "}");
            }
            catch { }
        }

        // TH3: mảng thuần [...]
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith("["))
        {
            try { return JsonUtility.FromJson<VRItemList>("{\"items\":" + trimmed + "}"); }
            catch { }
        }

        return new VRItemList();
    }

    // Rút mảng theo key (đọc ký tự, không regex ngoặc lồng)
    bool TryExtractArrayByKey(string json, string key, out string arrayJson)
    {
        arrayJson = null;
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;

        string low = json.ToLowerInvariant();
        string needle = "\"" + key.ToLowerInvariant() + "\"";
        int i = low.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return false;

        int colon = low.IndexOf(':', i);
        if (colon < 0) return false;

        int j = colon + 1;
        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
        if (j >= json.Length || json[j] != '[') return false;

        int start = j, depth = 0;
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

    // =================== UTILS ===================

    static string CombineUrl(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b ?? "";
        if (string.IsNullOrEmpty(b)) return a ?? "";
        if (!a.EndsWith("/")) a += "/";
        b = b.Replace("\\", "/").TrimStart('/');
        return a + b;
    }
}
