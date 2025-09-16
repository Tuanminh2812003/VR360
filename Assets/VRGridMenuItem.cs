// Assets/VRGridMenuItem.cs
using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

public class VRGridMenuItem : MonoBehaviour
{
    [Header("UI Refs")]
    public Image thumbnail;
    public TMP_Text titleText;
    public Button button;

    // sprite fallback khi không có ảnh
    [NonSerialized] public Sprite fallbackSprite;

    Action onClick;

    public void Bind(string title, string thumbUrl, string cacheDir, Action click, Sprite fallback, MonoBehaviour runner)
    {
        titleText.text = title ?? "";
        onClick = click;
        fallbackSprite = fallback;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        if (thumbnail != null)
        {
            // clear trước
            thumbnail.sprite = fallbackSprite;
            // bắt đầu tải (có cache)
            if (runner != null)
                runner.StartCoroutine(LoadThumbCoroutine(thumbUrl, cacheDir));
        }
    }

    IEnumerator LoadThumbCoroutine(string url, string cacheDir)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            thumbnail.sprite = fallbackSprite;
            yield break;
        }

        Directory.CreateDirectory(cacheDir);

        // tên file cache dựa trên hash của url
        string fileName = $"thumb_{url.GetHashCode():x}.png";
        string localPath = Path.Combine(cacheDir, fileName);

        // 1) ưu tiên load cache
        if (File.Exists(localPath))
        {
            var bytes = File.ReadAllBytes(localPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
            {
                thumbnail.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                yield break;
            }
        }

        // 2) nếu chưa có cache -> thử tải
        using (var req = UnityWebRequestTexture.GetTexture(url))
        {
            req.timeout = 15;
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                // thất bại -> dùng fallback
                thumbnail.sprite = fallbackSprite;
                yield break;
            }

            var tex = DownloadHandlerTexture.GetContent(req);
            if (tex != null)
            {
                // tạo sprite
                thumbnail.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                // lưu cache (PNG)
                try
                {
                    var png = tex.EncodeToPNG();
                    File.WriteAllBytes(localPath, png);
                }
                catch { /* ignore */ }
            }
            else
            {
                thumbnail.sprite = fallbackSprite;
            }
        }
    }
}
