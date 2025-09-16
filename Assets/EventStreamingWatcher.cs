using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class EventStreamingWatcher : MonoBehaviour
{
    [Header("API")]
    public string apiBase = "http://45.124.94.12:8080"; // không slash cuối
    public float intervalSec = 5f;

    [Header("Playback")]
    public VRGridMenu gridMenu;            // để tra URL theo _id
    public Skybox360Player player;         // player đang dùng
    public VRVideoOnDemand vod;            // downloader/phát theo id

    Coroutine _loop;
    string _lastEventId = "";
    string _lastStreamingId = "";

    void OnEnable()
    {
        StartLoop();
    }

    void OnDisable()
    {
        StopLoop();
    }

    public void StartLoop()
    {
        StopLoop();
        _loop = StartCoroutine(PollLoop());
    }

    public void StopLoop()
    {
        if (_loop != null) { StopCoroutine(_loop); _loop = null; }
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            string eid = AppConfigManager.GetEventId();
            if (string.IsNullOrEmpty(eid))
            {
                _lastEventId = "";
                _lastStreamingId = "";
                yield return new WaitForSeconds(intervalSec);
                continue;
            }

            if (_lastEventId != eid)
            {
                _lastEventId = eid;
                _lastStreamingId = ""; // reset so sánh khi đổi event
            }

            // GET /api/v1/event/detail/{id}
            string url = $"{apiBase}/api/v1/event/detail/{eid}";
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 15;
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = (req.result == UnityWebRequest.Result.Success);
#else
                bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (ok)
                {
                    string raw = req.downloadHandler.text ?? "";
                    string newStreaming = ExtractStreamingId(raw);

                    if (!string.IsNullOrEmpty(newStreaming) && newStreaming != _lastStreamingId)
                    {
                        _lastStreamingId = newStreaming;
                        AppConfigManager.SetStreamingVideoId(newStreaming);

                        // tìm url tương ứng trong list item hiện tại
                        string videoUrl = gridMenu ? gridMenu.GetUrlById(newStreaming) : null;
                        if (!string.IsNullOrEmpty(videoUrl) && player != null && vod != null)
                        {
                            // phát theo id (sẽ dùng local <id>.mp4 nếu có, không thì stream và tải nền)
                            vod.Play(player, newStreaming, videoUrl);
                        }
                        else
                        {
                            Debug.LogWarning("[Watcher] Không tìm thấy URL cho id=" + newStreaming);
                        }
                    }
                }
            }

            yield return new WaitForSeconds(intervalSec);
        }
    }

    // rút "data.streaming" từ JSON trả về
    string ExtractStreamingId(string json)
    {
        // Cực gọn: tìm cụm "streaming":"...". (Bạn có thể thay bằng JsonUtility wrapper nếu backend đổi format)
        const string key = "\"streaming\"";
        int i = json.IndexOf(key);
        if (i < 0) return "";
        i = json.IndexOf(':', i);
        if (i < 0) return "";
        // nhảy tới dấu quote
        int q1 = json.IndexOf('"', i + 1);
        if (q1 < 0) return "";
        int q2 = json.IndexOf('"', q1 + 1);
        if (q2 < 0) return "";
        return json.Substring(q1 + 1, q2 - q1 - 1);
    }
}
