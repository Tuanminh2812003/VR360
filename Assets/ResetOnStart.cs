using UnityEngine;

/// <summary>Reset 2 trường trong config khi app khởi động.</summary>
public class ResetOnStart : MonoBehaviour
{
    [Tooltip("Tự reset khi scene này khởi chạy")]
    public bool resetOnStart = true;

    void Awake()
    {
        if (resetOnStart)
            AppConfigManager.ResetFields();
    }
}
