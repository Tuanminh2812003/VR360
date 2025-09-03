using UnityEngine;

public class KeepGltfShaders : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Keep()
    {
        // Load materials mồi từ Resources
        var pbr = Resources.Load<Material>("PbrMetallicRoughness");
        var spec = Resources.Load<Material>("PbrSpecularGlossiness");
        var unlit = Resources.Load<Material>("Unlit");

        Debug.Log($"[KeepGltfShaders] PBR => {(pbr ? pbr.shader.name : "MISSING")}");
        Debug.Log($"[KeepGltfShaders] Specular => {(spec ? spec.shader.name : "MISSING")}");
        Debug.Log($"[KeepGltfShaders] Unlit => {(unlit ? unlit.shader.name : "MISSING")}");

        // Chỉ cần reference tới shader, Unity sẽ pack vào build
        if (pbr) Shader.WarmupAllShaders();
        if (spec) Shader.WarmupAllShaders();
        if (unlit) Shader.WarmupAllShaders();
    }
}
