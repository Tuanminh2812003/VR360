using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public static class DownloadManager
{
    // Thư mục lưu video cục bộ
    public static readonly string LocalFolder =
        Path.Combine(Application.persistentDataPath, "vr360");

    static DownloadManager()
    {
        if (!Directory.Exists(LocalFolder)) Directory.CreateDirectory(LocalFolder);
    }

    /// Tạo tên file ổn định từ URL (MD5) để tránh trùng/ghi đè.
    public static string UrlToLocalPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string md5 = MD5Hex(url);
        string ext = GuessExtensionFromUrl(url);
        return Path.Combine(LocalFolder, md5 + ext);
    }

    public static void CleanupStalePart(string localPath)
    {
        if (string.IsNullOrEmpty(localPath)) return;
        string part = localPath + ".part";
        try { if (File.Exists(part)) File.Delete(part); } catch { /* ignore */ }
    }

    /// Tải file nếu chưa có. Trả về đường dẫn local.
    /// Không chặn luồng chính: dùng async/await.
    public static async Task<string> EnsureDownloaded(string url, int timeoutSec = 30)
    {
        string local = UrlToLocalPath(url);
        if (string.IsNullOrEmpty(local)) throw new Exception("Invalid URL");

        // Đã có file hợp lệ?
        if (File.Exists(local) && new FileInfo(local).Length > 1024) return local;

        string part = local + ".part";
        CleanupStalePart(local);

        using (var req = UnityWebRequest.Get(url))
        {
            req.timeout = timeoutSec;
#if UNITY_2019_3_OR_NEWER
            req.downloadHandler = new DownloadHandlerFile(part) { removeFileOnAbort = true };
#else
            // Fallback cho phiên bản cũ: tải vào bộ nhớ rồi ghi ra đĩa
            req.downloadHandler = new DownloadHandlerBuffer();
#endif
            var op = req.SendWebRequest();

            // Đợi xong (không block)
            while (!op.isDone)
                await Task.Yield();

#if UNITY_2020_2_OR_NEWER
            bool ok = (req.result == UnityWebRequest.Result.Success);
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif
            if (!ok)
            {
                // Xảy ra lỗi -> dọn file tạm & ném lỗi
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                throw new Exception($"Download failed: {req.error} ({req.responseCode})");
            }

#if !UNITY_2019_3_OR_NEWER
            // Tự ghi ra file (nếu không có DownloadHandlerFile)
            try
            {
                File.WriteAllBytes(part, req.downloadHandler.data);
            }
            catch (Exception e)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                throw new Exception("Write temp failed: " + e.Message);
            }
#endif
        }

        // Đổi .part -> file chính
        try
        {
            if (File.Exists(local)) File.Delete(local);
            File.Move(part, local);
        }
        catch (Exception e)
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            throw new Exception("Finalize download failed: " + e.Message);
        }

        return local;
    }

    // ===== Helpers =====
    static string MD5Hex(string s)
    {
        using (var md5 = MD5.Create())
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            byte[] h = md5.ComputeHash(b);
            var sb = new StringBuilder(h.Length * 2);
            for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
            return sb.ToString();
        }
    }

    static string GuessExtensionFromUrl(string url)
    {
        // Lấy đuôi từ URL nếu có, fallback .mp4
        try
        {
            var u = new Uri(url);
            string last = Path.GetFileName(u.LocalPath);
            string ext = Path.GetExtension(last);
            if (!string.IsNullOrEmpty(ext)) return ext.ToLowerInvariant();
        }
        catch { }
        return ".mp4";
    }
}
