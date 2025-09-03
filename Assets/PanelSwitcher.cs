using UnityEngine;

public class PanelSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class NamedPanel
    {
        public string name;         // "Main", "Artifacts", ...
        public GameObject panel;    // object panel tương ứng
    }

    [Header("Danh sách panel")]
    public NamedPanel[] panels;

    [Header("Tùy chọn")]
    public string defaultPanelName = "Main"; // panel mở mặc định

    // Nếu bạn có script VRMenuSummoner trên panel Menu chính, kéo vào để snap lại khi quay về
    public VRMenuSummoner menuSummoner;

    void Start()
    {
        // Bật panel mặc định
        if (!string.IsNullOrEmpty(defaultPanelName))
            Open(defaultPanelName);
    }

    public void Open(string panelName)
    {
        // Bật panel có tên khớp, tắt panel còn lại
        foreach (var p in panels)
        {
            if (!p.panel) continue;
            bool active = (p.name == panelName);
            p.panel.SetActive(active);
        }

        // Nếu quay lại menu chính thì cho snap lại trước mặt (tuỳ bạn)
        if (panelName == "Main" && menuSummoner)
        {
            menuSummoner.ShowMenu();
        }
    }

    // Dùng cho Button.OnClick (nếu thích gọi theo index)
    public void OpenByIndex(int idx)
    {
        if (idx < 0 || idx >= panels.Length) return;
        Open(panels[idx].name);
    }
}
