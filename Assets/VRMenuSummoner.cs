using UnityEngine;
using UnityEngine.InputSystem; // Input System

[DefaultExecutionOrder(1000)]
public class VRMenuSummoner : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("HMD/Main Camera. Bỏ trống -> tự lấy Camera.main")]
    public Transform hmd;

    [Tooltip("Gốc UI cần bật/tắt (Canvas hoặc panel). Bắt buộc set cho chắc.")]
    public GameObject menuRoot;

    [Header("Placement")]
    [Tooltip("Khoảng cách trước mặt (m)")]
    public float distance = 1.6f;

    [Tooltip("Độ lệch cao/thấp so với mắt (m)")]
    public float heightOffset = 0.0f;

    [Tooltip("Chỉ dùng yaw (bỏ pitch/roll) để đỡ chóng mặt")]
    public bool yawOnly = true;

    [Header("Input (optional)")]
    [Tooltip("Gán action (ví dụ Primary/Menu Button). Có thể để trống nếu chỉ gọi từ UI.")]
    public InputActionProperty toggleAction;

    [Header("Behavior")]
    [Tooltip("Ẩn menu khi khởi động")]
    public bool startHidden = true;

    [Tooltip("Số frame đầu bám hướng đầu khi vào scene (0 = tắt)")]
    public int snapFramesOnStart = 0;

    [Header("Orientation")]
    [Tooltip("Điều chỉnh yaw bổ sung (độ). 180 = quay mặt về người dùng. Nếu còn ngược, thử 0.")]
    public float yawAdjustDeg = 0f;

    // ===== Internal =====
    int _snapLeft;

    void Awake()
    {
        if (hmd == null && Camera.main) hmd = Camera.main.transform;

        if (!menuRoot)
        {
            // Nếu không set, cố gắng dùng object hiện tại
            menuRoot = gameObject;
        }

        if (startHidden && menuRoot) menuRoot.SetActive(false);
    }

    void Start()
    {
        _snapLeft = Mathf.Max(0, snapFramesOnStart);
        StartCoroutine(SnapAtStart());
    }

    System.Collections.IEnumerator SnapAtStart()
    {
        // chờ 1 frame để HMD/camera cập nhật pose chính xác
        yield return null;

        if (hmd == null && Camera.main) hmd = Camera.main.transform;
        if (hmd == null) yield break;

        // Nếu không ẩn khi khởi động -> hiển thị ngay đúng trước mặt
        if (!startHidden)
        {
            ShowMenuInFront();
        }
        else
        {
            // Trường hợp startHidden=true nhưng menu đang active sẵn trong scene
            if (menuRoot && menuRoot.activeSelf) ShowMenuInFront();
        }
    }

    void LateUpdate()
    {
        // Tuỳ chọn: bám theo đầu trong vài frame đầu
        if (_snapLeft > 0)
        {
            ShowMenuInFront();
            _snapLeft--;
        }
    }

    void OnEnable()
    {
        if (toggleAction.reference != null)
        {
            toggleAction.action.Enable();
            toggleAction.action.performed += OnToggle;
        }
    }

    void OnDisable()
    {
        if (toggleAction.reference != null)
        {
            toggleAction.action.performed -= OnToggle;
            toggleAction.action.Disable();
        }
    }

    void OnToggle(InputAction.CallbackContext _)
    {
        ToggleMenu();
    }

    // ===== Public API (gọi từ Button/Script khác) =====
    public void ToggleMenu()
    {
        if (!menuRoot) return;
        if (menuRoot.activeSelf) HideMenu();
        else ShowMenuInFront();
    }

    public void ShowMenu()
    {
        ShowMenuInFront();
    }

    public void HideMenu()
    {
        if (!menuRoot) return;

        var cg = menuRoot.GetComponent<CanvasGroup>();
        if (cg)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }
        else
        {
            menuRoot.SetActive(false);
        }
    }

    // ===== Core: đặt menu trước mặt rồi bật =====
    public void ShowMenuInFront()
    {
        if (!menuRoot) return;
        if (hmd == null && Camera.main) hmd = Camera.main.transform;
        if (hmd == null) return;

        // Lấy forward/yaw mong muốn
        Vector3 forward = hmd.forward;
        if (yawOnly)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
        }

        // Vị trí: trước mặt theo forward, cộng offset cao/thấp
        Vector3 pos = hmd.position + forward * distance + Vector3.up * heightOffset;

        // Hướng: quay mặt về người dùng (nhìn ngược với forward)
        float baseYaw = yawOnly ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : hmd.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(0f, baseYaw + yawAdjustDeg + 360f, 0f);

        Transform t = menuRoot.transform;
        t.SetPositionAndRotation(pos, rot);

        // Bật lại tương tác nếu dùng CanvasGroup
        var cg = menuRoot.GetComponent<CanvasGroup>();
        if (cg)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
            if (!menuRoot.activeSelf) menuRoot.SetActive(true); // phòng trường hợp bị tắt hẳn
        }
        else
        {
            menuRoot.SetActive(true);
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!hmd) return;

        Vector3 f = hmd.forward;
        if (yawOnly)
        {
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
            f.Normalize();
        }

        Vector3 p = hmd.position + f * distance + Vector3.up * heightOffset;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(hmd.position, p);
        Gizmos.DrawWireSphere(p, 0.03f);
    }
#endif
}
