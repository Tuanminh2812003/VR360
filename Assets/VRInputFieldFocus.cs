using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_InputField))]
public class VRInputFieldFocus : MonoBehaviour
{
    public VRNumpad3D numpad;

    TMP_InputField _field;

    void Reset() { _field = GetComponent<TMP_InputField>(); }
    void Awake() { if (!_field) _field = GetComponent<TMP_InputField>(); }

    void OnEnable()
    {
        if (_field == null) _field = GetComponent<TMP_InputField>();
        _field.onSelect.AddListener(OnSelected);
    }

    void OnDisable()
    {
        if (_field != null) _field.onSelect.RemoveListener(OnSelected);
    }

    void OnSelected(string _)
    {
        if (!numpad)
            numpad = FindObjectOfType<VRNumpad3D>(true);

        if (numpad)
        {
            numpad.ShowInFront();   // đặt trước mặt & quay đúng hướng
            numpad.ShowFor(_field); // gán input hiện tại
        }
        else
        {
            Debug.LogWarning("[VRNumpad] Không tìm thấy VRNumpad3D trong scene.");
        }
    }
}
