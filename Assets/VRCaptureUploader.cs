using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class VRCaptureUploader : MonoBehaviour
{
    [Header("API Settings")]
    public string uploadUrl = "http://45.124.94.12:8080/api/v1/mediafile/upload";
    public string folder = "streaming";
    public string createdBy = "67b4bda7a1eee9acb094b044"; // id user
    public string thumbnail = "public/thumbnail/1757741783961.png";
    public string title = "VR Stream Frame";

    [Header("Capture")]
    public Camera captureCamera;   // Camera để chụp (ví dụ MainCamera)
    public int width = 1280;
    public int height = 720;
    public float interval = 1f;    // chụp mỗi 5s

    private RenderTexture rt;
    private Texture2D tex;

    void Start()
    {
        if (captureCamera == null) captureCamera = Camera.main;

        rt = new RenderTexture(width, height, 24);
        tex = new Texture2D(width, height, TextureFormat.RGB24, false);

        StartCoroutine(CaptureLoop());
    }

    IEnumerator CaptureLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(interval);
            yield return StartCoroutine(CaptureAndUpload());
        }
    }

    IEnumerator CaptureAndUpload()
    {
        // Render vào RenderTexture
        captureCamera.targetTexture = rt;
        captureCamera.Render();

        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        captureCamera.targetTexture = null;
        RenderTexture.active = null;

        // JPG nén 70% (chỉ ~50-150KB/ảnh thay vì vài MB)
        byte[] jpg = tex.EncodeToJPG(70);

        WWWForm form = new WWWForm();
        form.AddBinaryData("files", jpg, "frame.jpg", "image/jpeg");
        form.AddField("folder", folder);
        form.AddField("created_by", createdBy);
        form.AddField("thumbnail", thumbnail);
        form.AddField("title", title);

        using (UnityWebRequest www = UnityWebRequest.Post(uploadUrl, form))
        {
            yield return www.SendWebRequest();
#if UNITY_2020_2_OR_NEWER
        if (www.result != UnityWebRequest.Result.Success)
#else
            if (www.isHttpError || www.isNetworkError)
#endif
            {
                Debug.LogError("[Uploader] Error: " + www.error);
            }
            else
            {
                Debug.Log("[Uploader] Uploaded " + jpg.Length / 1024 + " KB: " + www.downloadHandler.text);
            }
        }
    }
}
