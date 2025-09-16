using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(1001)]
public class VRNumpad3D : MonoBehaviour
{
    [Header("UI Roots")]
    [Tooltip("Canvas/Panel gốc của numpad (sẽ SetActive). Mặc định = gameObject hiện tại")]
    public GameObject root;

    [Tooltip("Nơi để sinh các phím (Grid). Nếu để trống sẽ dùng chính RectTransform của script")]
    public RectTransform keysRoot;

    [Header("Key Prefab")]
    [Tooltip("Prefab Button đơn giản có TMP_Text con. Nếu để trống sẽ tạo runtime.")]
    public Button keyPrefab;

    [Header("Look & Feel")]
    public Vector2 cellSize = new Vector2(160, 120);
    public Vector2 spacing = new Vector2(12, 12);
    public float cornerRadius = 16f;

    [Header("Repeat")]
    [Tooltip("Giữ Backspace bao lâu thì bắt đầu lặp (giây)")]
    public float backspaceHoldDelay = 0.4f;
    [Tooltip("Số lần lặp Backspace mỗi giây sau khi trễ")]
    public float backspaceRepeatPerSec = 16f;

    [Header("Placement Helpers (tùy chọn)")]
    public Transform hmd;      // nếu muốn dùng ShowInFront()
    public float distance = 1.2f;
    public float heightOffset = -0.05f;
    public bool yawOnly = true;
    public float yawAdjustDeg = 0f;
    [Tooltip("Nếu true, bàn phím luôn quay mặt về HMD theo trục Y")]
    public bool faceUser = true;

    [Header("Debounce")]
    [Tooltip("Lọc 2 lần bấm liên tiếp để tránh double-callback UI")]
    [Range(0.05f, 0.30f)] public float clickDebounce = 0.12f;

    // ---------- runtime ----------
    TMP_InputField _tmpIF;                  // mục tiêu hiện tại (TMP)
    TMP_InputField _lastIf;                 // đã từng focus gần đây
    Coroutine _repeatCo;

    readonly List<Button> _spawned = new();
    GridLayoutGroup _grid;

    // chống double-click
    float _lastClickTS = -999f;
    string _lastClickKey = null;

    void Reset()
    {
        if (!root) root = gameObject;
        if (!keysRoot) keysRoot = GetComponent<RectTransform>();
    }

    void Awake()
    {
        if (!root) root = gameObject;
        if (!keysRoot) keysRoot = GetComponent<RectTransform>();
        if (!hmd && Camera.main) hmd = Camera.main.transform;

        EnsureGrid();
        EnsurePrefab();
        BuildIfEmpty();
        Hide();
    }

    // ===================== Public API =====================

    /// <summary>Mở numpad cho input này (và đặt caret cuối)</summary>
    public void ShowFor(TMP_InputField field)
    {
        _tmpIF = field;
        _lastIf = field;

        if (_tmpIF)
        {
            _tmpIF.caretPosition = _tmpIF.text?.Length ?? 0;
            _tmpIF.selectionStringAnchorPosition = _tmpIF.caretPosition;
            _tmpIF.selectionStringFocusPosition = _tmpIF.caretPosition;
        }

        if (root && !root.activeSelf) root.SetActive(true);
    }

    /// <summary>Ẩn numpad</summary>
    public void Hide()
    {
        if (_repeatCo != null) { StopCoroutine(_repeatCo); _repeatCo = null; }
        if (root) root.SetActive(false);
    }

    /// <summary>Đặt numpad trước mặt người dùng</summary>
    public void ShowInFront()
    {
        if (!root) return;
        if (!hmd && Camera.main) hmd = Camera.main.transform;
        if (!hmd) { root.SetActive(true); return; }

        Vector3 fwd = hmd.forward;
        if (yawOnly)
        {
            fwd.y = 0;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            fwd.Normalize();
        }

        Vector3 pos = hmd.position + fwd * distance + Vector3.up * heightOffset;

        Quaternion rot;
        if (faceUser)
        {
            Vector3 toHmd = hmd.position - pos; toHmd.y = 0;
            if (toHmd.sqrMagnitude < 1e-6f) toHmd = -fwd;
            rot = Quaternion.LookRotation(toHmd.normalized, Vector3.up);
        }
        else
        {
            float yaw = hmd.eulerAngles.y;
            if (yawOnly) yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            rot = Quaternion.Euler(0, yaw + yawAdjustDeg, 0);
        }

        rot *= Quaternion.Euler(0, 180f, 0);

        root.transform.SetPositionAndRotation(pos, rot);
        root.SetActive(true);
    }

    // ===================== Building =====================

    void EnsureGrid()
    {
        _grid = keysRoot ? keysRoot.GetComponent<GridLayoutGroup>() : null;
        if (!_grid && keysRoot)
            _grid = keysRoot.gameObject.AddComponent<GridLayoutGroup>();

        if (_grid)
        {
            _grid.cellSize = cellSize;
            _grid.spacing = spacing;
            _grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            _grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = 3; // bố cục 3 cột
        }
    }

    void EnsurePrefab()
    {
        if (keyPrefab != null) return;

        // tạo button tối giản runtime
        var go = new GameObject("KeyButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = cellSize;

        var img = go.GetComponent<Image>();
        img.color = new Color(1, 1, 1, 0.12f);
        img.type = Image.Type.Sliced;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = "0";

        keyPrefab = go.GetComponent<Button>();
        keyPrefab.targetGraphic = img;
        keyPrefab.transition = Selectable.Transition.ColorTint;
    }

    void BuildIfEmpty()
    {
        if (!keysRoot) return;
        if (keysRoot.childCount > 0) return;

        // 1–9 / CLR 0 ⌫ / Cancel OK
        string[] labels =
        {
            "1","2","3",
            "4","5","6",
            "7","8","9",
            "CLR","0","⌫",
            "Cancel","OK"
        };

        foreach (var lb in labels)
        {
            var b = CreateKey(lb);

            // tắt Navigation để tránh UI tự chuyển focus bắn thêm sự kiện
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
        }
    }

    Button CreateKey(string label)
    {
        var b = Instantiate(keyPrefab, keysRoot);
        _spawned.Add(b);

        // set label
        var tmp = b.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp) tmp.text = label;

        // wire event
        b.onClick.AddListener(() => OnKeyPressed(label));

        // backspace giữ-để-lặp
        if (label == "⌫")
        {
            var hold = b.gameObject.GetComponent<VRNumpadHoldable>();
            if (!hold) hold = b.gameObject.AddComponent<VRNumpadHoldable>();
            hold.onHoldStart = () =>
            {
                if (_repeatCo == null) _repeatCo = StartCoroutine(RepeatBackspace());
            };
            hold.onHoldEnd = () =>
            {
                if (_repeatCo != null) { StopCoroutine(_repeatCo); _repeatCo = null; }
            };
        }

        return b;
    }

    IEnumerator RepeatBackspace()
    {
        yield return new WaitForSeconds(backspaceHoldDelay);
        var dt = 1f / Mathf.Max(1f, backspaceRepeatPerSec);
        while (true)
        {
            BackspaceOnce();
            yield return new WaitForSeconds(dt);
        }
    }

    // ===================== Key logic =====================

    void OnKeyPressed(string key)
    {
        // Debounce chống double-callback
        if (Time.unscaledTime - _lastClickTS < clickDebounce && key == _lastClickKey)
            return;
        _lastClickTS = Time.unscaledTime;
        _lastClickKey = key;

        if (key == "OK") Submit();
        else if (key == "Cancel") Hide();
        else if (key == "CLR") ClearAll();
        else if (key == "⌫") BackspaceOnce();
        else InsertDigit(key);
    }

    void InsertDigit(string digit)
    {
        if (_tmpIF == null) return;
        if (digit.Length != 1 || digit[0] < '0' || digit[0] > '9') return;

        var txt = _tmpIF.text ?? "";
        int a = Mathf.Min(_tmpIF.selectionStringAnchorPosition, _tmpIF.selectionStringFocusPosition);
        int b = Mathf.Max(_tmpIF.selectionStringAnchorPosition, _tmpIF.selectionStringFocusPosition);
        if (a == b) a = b = _tmpIF.caretPosition;

        txt = txt.Remove(a, b - a).Insert(a, digit);
        _tmpIF.text = txt;
        _tmpIF.caretPosition = a + 1;
        _tmpIF.selectionStringAnchorPosition = _tmpIF.caretPosition;
        _tmpIF.selectionStringFocusPosition = _tmpIF.caretPosition;
    }

    void BackspaceOnce()
    {
        if (_tmpIF == null) return;
        var txt = _tmpIF.text ?? "";
        int a = Mathf.Min(_tmpIF.selectionStringAnchorPosition, _tmpIF.selectionStringFocusPosition);
        int b = Mathf.Max(_tmpIF.selectionStringAnchorPosition, _tmpIF.selectionStringFocusPosition);
        if (a != b)
        {
            txt = txt.Remove(a, b - a);
            _tmpIF.text = txt;
            _tmpIF.caretPosition = a;
        }
        else if (a > 0)
        {
            txt = txt.Remove(a - 1, 1);
            _tmpIF.text = txt;
            _tmpIF.caretPosition = a - 1;
        }

        _tmpIF.selectionStringAnchorPosition = _tmpIF.caretPosition;
        _tmpIF.selectionStringFocusPosition = _tmpIF.caretPosition;
    }

    void ClearAll()
    {
        if (_tmpIF == null) return;
        _tmpIF.text = "";
        _tmpIF.caretPosition = 0;
        _tmpIF.selectionStringAnchorPosition = 0;
        _tmpIF.selectionStringFocusPosition = 0;
    }

    void Submit()
    {
        // nếu cần, có thể bắn UnityEvent ở đây
        Hide();
    }

    // tiện ích công khai: mở numpad trước mặt và gán input cũ nếu có
    public void ToggleForLastOrShowInFront()
    {
        if (root && root.activeSelf) Hide();
        else if (_lastIf) ShowFor(_lastIf);
        else ShowInFront();
    }
}

/* Helper phát hiện giữ nút cho Backspace */
public class VRNumpadHoldable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public System.Action onHoldStart;
    public System.Action onHoldEnd;
    bool _holding;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (_holding) return;
        _holding = true;
        onHoldStart?.Invoke();
    }
    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_holding) return;
        _holding = false;
        onHoldEnd?.Invoke();
    }
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_holding) return;
        _holding = false;
        onHoldEnd?.Invoke();
    }
}
