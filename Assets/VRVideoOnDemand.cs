using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class VRVideoOnDemand : MonoBehaviour
{
    [Header("Save Folder (under persistentDataPath)")]
    public string subFolder = "vr360";          // => <persistent>/vr360/
    public string fileExt = ".mp4";             // tên file lưu là <_id>.mp4
    public float timeoutSec = 60f;

    // chống tải trùng 1 id
    static readonly HashSet<string> InFlight = new HashSet<string>();

    string Root => Path.Combine(Application.persistentDataPath, subFolder);

    void Awake()
    {
        try { if (!Directory.Exists(Root)) Directory.CreateDirectory(Root); }
        catch (Exception e) { Debug.LogWarning("[VOD] Create dir fail: " + e.Message); }
    }

    public string GetLocalPath(string id) => Path.Combine(Root, id + fileExt);

    /// <summary>
    /// Nếu đã có file id.mp4 -> phát local.
    /// Nếu chưa -> stream từ url để xem ngay, đồng thời tải nền về id.mp4 (không tải trùng).
    /// </summary>
    public void Play(Skybox360Player player, string id, string url)
    {
        if (player == null || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url))
        {
            Debug.LogWarning("[VOD] Missing player/id/url");
            return;
        }

        string local = GetLocalPath(id);
        if (File.Exists(local))
        {
            _ = player.PlayAbsolutePathAsync(local);
            return;
        }

        // Chưa có: stream ngay từ url
        _ = player.PlayUrlAsync(url);

        // và tải nền (nếu chưa tải)
        if (!InFlight.Contains(id))
            StartCoroutine(DownloadToIdCoroutine(id, url));
    }

    /// <summary>Tải riêng về id.mp4 (không phát)</summary>
    public void DownloadOnly(string id, string url)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)) return;
        if (File.Exists(GetLocalPath(id))) return;
        if (!InFlight.Contains(id))
            StartCoroutine(DownloadToIdCoroutine(id, url));
    }

    IEnumerator DownloadToIdCoroutine(string id, string url)
    {
        string finalPath = GetLocalPath(id);
        string tmpPath = finalPath + ".part";

        if (File.Exists(finalPath)) yield break; // có rồi
        if (InFlight.Contains(id)) yield break;

        InFlight.Add(id);
        try
        {
            // xoá .part cũ nếu có
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }

            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.CeilToInt(timeoutSec);
#if UNITY_2020_2_OR_NEWER
                req.downloadHandler = new DownloadHandlerFile(tmpPath, false); // overwrite
#else
                req.downloadHandler = new DownloadHandlerFile(tmpPath);
#endif
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = (req.result == UnityWebRequest.Result.Success);
#else
                bool ok = !req.isNetworkError && !req.isHttpError;
#endif
                if (!ok)
                {
                    Debug.LogWarning($"[VOD] Download fail {id}: {req.error}");
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    yield break;
                }
            }

            // move .part -> .mp4
            try
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(tmpPath, finalPath);
                Debug.Log($"[VOD] Saved {Path.GetFileName(finalPath)}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[VOD] Move file failed: " + e.Message);
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }
        finally
        {
            InFlight.Remove(id);
        }
    }
}
