// Assets/ArtifactController.cs
using System.Threading.Tasks;
using UnityEngine;

public class ArtifactController : MonoBehaviour
{
    [Header("UI & Panels")]
    public VRMenuSummoner menuSummoner;     // gắn VRMenuSummoner trên Panel (menu chính)
    public PanelSwitcher panelSwitcher;     // nếu bạn dùng PanelSwitcher để chuyển panel
    public string artifactsPanelName = "Artifacts"; // tên panel hiện vật trong PanelSwitcher (nếu có)

    [Header("Video 360")]
    public Skybox360Player skybox360;       // gắn instance Skybox360Player (VideoPlayerGO)

    [Header("Artifact Stage")]
    public GameObject artifactStage;        // ArtifactStage (Empty) – bật/tắt khi xem
    public GlbArtifactLoader loader;        // gắn component trên ArtifactStage
    public VRArtifactOrbit orbit;           // gắn component trên ArtifactStage

    [Header("Options")]
    public bool placeStageEveryFrame = true; // VRArtifactOrbit sẽ đặt sân khấu trước mặt mỗi frame

    void Awake()
    {
        if (artifactStage) artifactStage.SetActive(false);
    }

    // ====== PUBLIC API: gọi từ Button/Ô Grid ======

    /// <summary>Mở một mẫu vật từ StreamingAssets (vd: "trong_ngoc_lu.glb").</summary>
    public async void OpenArtifactFromStreamingAssets(string glbFileName)
    {
        await OpenCommon(async () => { await loader.LoadFromStreamingAssets(glbFileName); });
    }

    /// <summary>Mở mẫu vật từ đường dẫn tuyệt đối (nếu bạn duyệt file ngoài).</summary>
    public async void OpenArtifactAbsolute(string fullPath)
    {
        await OpenCommon(async () => { await loader.LoadAbsolutePath(fullPath); });
    }

    /// <summary>Đóng khu trưng bày, dọn mẫu vật, có/không gọi lại menu tuỳ nhu cầu.</summary>
    public void CloseArtifact(bool showMenu = false)
    {
        if (loader) loader.Unload();
        if (artifactStage) artifactStage.SetActive(false);

        // Giữ skybox trắng/idle cho nền (tuỳ bạn muốn resume video hay không)
        if (skybox360) skybox360.StopPlaybackToIdle();

        if (showMenu && menuSummoner) menuSummoner.ShowMenu();
    }

    // ====== CORE ======
    async Task OpenCommon(System.Func<Task> loadTask)
    {
        if (!artifactStage || !loader || !orbit)
        {
            Debug.LogError("[ArtifactController] Chưa gán đủ tham chiếu (artifactStage/loader/orbit)");
            return;
        }

        // 2) Tắt video 360 (skybox -> màu idle)
        if (skybox360) skybox360.StopPlaybackToIdle();

        // 3) Bật panel hiện vật (nếu bạn dùng PanelSwitcher để đổi panel UI)
        if (panelSwitcher && !string.IsNullOrEmpty(artifactsPanelName))
        {
            panelSwitcher.Open(artifactsPanelName);
        }

        // 4) Bật ArtifactStage + đặt trước mặt người dùng
        artifactStage.SetActive(true);
        orbit.ResetView(); // đưa góc nhìn mẫu vật về mặc định
        orbit.SnapOnceInFront();

        // 5) Load GLB (fit/center tự xử lý trong loader)
        await loadTask.Invoke();

        // 6) Reset lại góc (sau khi fit xong)
        orbit.ResetView();

        // 7) Nếu muốn stage đứng yên (không theo đầu), tắt đặt theo frame
        orbit.enabled = placeStageEveryFrame;
    }
}
