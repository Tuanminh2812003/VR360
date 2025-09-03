using UnityEngine;
using UnityEngine.InputSystem;

public class VRArtifactOrbit : MonoBehaviour
{
    [Header("Refs")]
    public Transform hmd;           // HMD/Main Camera
    public Transform stage;         // ArtifactStage (bệ đặt mẫu vật) - thường là chính GameObject gắn script
    public Transform modelRoot;     // ModelRoot (đối tượng để xoay)

    [Header("Placement")]
    public float distance = 1.2f;
    public float heightOffset = -0.2f;
    public bool yawOnly = true;

    [Tooltip("Bật = luôn bám hướng HMD mỗi frame. Tắt = chỉ đặt 1 lần (snap) rồi đứng yên.")]
    public bool followHead = true;

    [Header("Controls (Input System)")]
    [Tooltip("Vector2: joystick/phím A-D W-S")]
    public InputActionProperty rotateAction;   // Value/Vector2
    [Tooltip("Vector2 (joystick) hoặc Axis (1D) với composite Up/Down")]
    public InputActionProperty zoomAction;     // Value/Vector2 hoặc Value/Axis

    [Header("Speeds & Limits")]
    public float rotSpeedYaw = 120f;
    public float rotSpeedPitch = 90f;
    public float minPitch = -60f, maxPitch = 60f;

    public float zoomSpeed = 1.2f;
    public float minDistance = 0.5f, maxDistance = 2.5f;

    [Header("Zoom read preference")]
    [Tooltip("True: ưu tiên Y (Up/Down). False: ưu tiên X (Left/Right).")]
    public bool preferYForZoom = true;

    float _pitch;

    void Awake()
    {
        if (!hmd && Camera.main) hmd = Camera.main.transform;
        if (!stage) stage = transform;
    }

    void OnEnable()
    {
        if (rotateAction.reference) rotateAction.action.Enable();
        if (zoomAction.reference)   zoomAction.action.Enable();
    }
    void OnDisable()
    {
        if (rotateAction.reference) rotateAction.action.Disable();
        if (zoomAction.reference)   zoomAction.action.Disable();
    }

    void LateUpdate()
    {
        if (!hmd || !stage) return;

        // 1) Đặt sân khấu
        if (followHead)
            PlaceInFrontOfHead();   // bám theo đầu
        // nếu followHead=false thì giữ nguyên vị trí đã snap trước đó

        if (!modelRoot) return;

        // 2) Xoay
        Vector2 rot = ReadVector2Safe(rotateAction);
        float yawDelta   = rot.x * rotSpeedYaw    * Time.deltaTime;
        float pitchDelta = -rot.y * rotSpeedPitch * Time.deltaTime;

        modelRoot.Rotate(Vector3.up, yawDelta, Space.Self);
        _pitch = Mathf.Clamp(_pitch + pitchDelta, minPitch, maxPitch);
        var e = modelRoot.localEulerAngles;
        float yaw = e.y;
        modelRoot.localRotation = Quaternion.Euler(_pitch, yaw, 0f);

        // 3) Zoom (đổi distance)
        float zoom = ReadZoomMixed(zoomAction, preferYForZoom);
        distance = Mathf.Clamp(distance - zoom * zoomSpeed * Time.deltaTime, minDistance, maxDistance);
    }

    /// <summary>Đặt stage trước mặt HMD theo thông số distance/heightOffset.</summary>
    public void PlaceInFrontOfHead()
    {
        if (!hmd || !stage) return;

        Vector3 fwd = hmd.forward;
        if (yawOnly)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
        }

        stage.position = hmd.position + fwd * distance + Vector3.up * heightOffset;
        stage.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
    }

    /// <summary>Snap 1 lần ngay lúc gọi (không bật follow).</summary>
    public void SnapOnceInFront()
    {
        PlaceInFrontOfHead();
        followHead = false;
    }

    public void ResetView(float newDistance = -1f)
    {
        _pitch = 0f;
        if (modelRoot) modelRoot.localRotation = Quaternion.identity;
        if (newDistance > 0f) distance = Mathf.Clamp(newDistance, minDistance, maxDistance);
    }

    // ===== Helpers: đọc action an toàn =====
    static Vector2 ReadVector2Safe(InputActionProperty a)
    {
        if (!a.reference) return Vector2.zero;
        try { return a.action.ReadValue<Vector2>(); }
        catch { return Vector2.zero; }
    }

    static float ReadZoomMixed(InputActionProperty a, bool preferY)
    {
        if (!a.reference) return 0f;
        try
        {
            Vector2 v = a.action.ReadValue<Vector2>();
            return preferY
                ? (Mathf.Abs(v.y) >= Mathf.Abs(v.x) ? v.y : 0f)
                : (Mathf.Abs(v.x) >= Mathf.Abs(v.y) ? v.x : 0f);
        }
        catch
        {
            try { return a.action.ReadValue<float>(); }
            catch { return 0f; }
        }
    }
}
