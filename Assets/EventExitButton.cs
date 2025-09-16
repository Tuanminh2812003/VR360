// Assets/EventExitButton.cs
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class EventExitButton : MonoBehaviour
{
    [Header("Refs")]
    public VRGridMenu gridMenu;          // component VRGridMenu đang dùng để hiển thị list video
    public Skybox360Player player;       // để dừng phát video (tuỳ)
    public GameObject eventListPanel;    // Panel hiển thị danh sách sự kiện (Scroll View Event)
    public GameObject videoListPanel;    // Panel danh sách video của event

    [Header("Main JSON")]
    public string mainJsonFileName = "vr360_list.json"; // file tổng ở persistentDataPath

    // Gắn hàm này vào OnClick của nút Thoát
    public void ExitEvent()
    {
        // 1) Dừng phát video nếu đang phát
        if (player) player.StopPlaybackToIdle();

        // 2) Chuyển panel
        if (videoListPanel) videoListPanel.SetActive(false);
        if (eventListPanel) eventListPanel.SetActive(true);

        // 3) Ép VRGridMenu đọc lại file tổng & rebuild
        if (gridMenu != null)
        {
            // nếu VRGridMenu có field jsonFileName thì set lại:
            gridMenu.jsonFileName = mainJsonFileName;

            // nếu VRGridMenu có API LoadFromJson(fullPath) thì bạn có thể dùng:
            // var full = Path.Combine(Application.persistentDataPath, mainJsonFileName);
            // gridMenu.LoadFromJson(full);

            gridMenu.Rebuild(); // rebuild lại list từ file tổng
        }
    }
}
