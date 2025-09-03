// Assets/GlbArtifactLoader.cs
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using GLTFast;

public class GlbArtifactLoader : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Nơi chứa instance GLB sau khi load (thường là một empty tên ModelRoot)")]
    public Transform modelRoot;

    [Header("Fit options")]
    [Tooltip("Kích cỡ cạnh lớn nhất sau khi fit (m)")]
    public float targetSize = 0.6f;
    [Tooltip("Đưa tâm model về (0,0,0) của modelRoot")]
    public bool centerOnLoad = true;

    [Header("Runtime Options")]
    [Tooltip("Thêm MeshCollider cho mọi MeshFilter sau khi load (phục vụ raycast/grab)")]
    public bool addMeshColliders = true;

    [Tooltip("Ưu tiên dùng Unlit (thay vì PBR) khi cứu shader")]
    public bool preferUnlit = true;

    GameObject _currentGO;

    // ---------- PUBLIC API ----------

    public async Task<GameObject> LoadFromStreamingAssets(string fileName)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        return await LoadAbsolutePath(path);
    }

    public async Task<GameObject> LoadAbsolutePath(string fullPath)
    {
        if (modelRoot == null) modelRoot = transform;

        if (_currentGO) Destroy(_currentGO);

        var gltf = new GltfImport();
        var uri = new Uri(fullPath);
        bool ok = await gltf.Load(uri);
        if (!ok)
        {
            Debug.LogError($"[GLB] Load failed: {fullPath}");
            return null;
        }

        _currentGO = new GameObject("GLB_Instance");
        _currentGO.transform.SetParent(modelRoot, false);

        await gltf.InstantiateMainSceneAsync(_currentGO.transform);

        // Cứu material theo ưu tiên Unlit
        FixMaterials(_currentGO, preferUnlit);

        if (addMeshColliders) AddMeshCollidersRecursive(_currentGO);

        FitAndCenter(_currentGO);

        return _currentGO;
    }

    public void Unload()
    {
        if (_currentGO) Destroy(_currentGO);
        _currentGO = null;
    }

    // ---------- HELPERS ----------

    void AddMeshCollidersRecursive(GameObject root)
    {
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
            var col = mf.GetComponent<MeshCollider>();
            if (!col) col = mf.gameObject.AddComponent<MeshCollider>();
            col.sharedMesh = mf.sharedMesh;
        }
    }

    public void FitAndCenter(GameObject go)
    {
        if (!go) return;

        var bWorld = CalcWorldBounds(go);

        if (centerOnLoad)
        {
            var pivot = new GameObject("Pivot");
            pivot.transform.SetParent(modelRoot, false);
            go.transform.SetParent(pivot.transform, true);

            var centerLocal = modelRoot.InverseTransformPoint(bWorld.center);
            go.transform.localPosition = -centerLocal;

            go = pivot;
            bWorld = CalcWorldBounds(go);
        }

        float maxSize = Mathf.Max(bWorld.size.x, Mathf.Max(bWorld.size.y, bWorld.size.z));
        if (maxSize > 1e-4f)
        {
            float s = targetSize / maxSize;
            go.transform.localScale *= s;
        }
    }

    Bounds CalcWorldBounds(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 1e-3f);

        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // ==================== MATERIAL RESCUE ====================

    static void FixMaterials(GameObject root, bool preferUnlit)
    {
        var pbr   = Shader.Find("glTF/PbrMetallicRoughness"); // Built-in RP gltFast
        var unlit = Shader.Find("glTF/Unlit");
        var std   = Shader.Find("Standard");

        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.materials; // instance materials (safe to modify)
            bool changed = false;

            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (!m) continue;

                var sh = m.shader;
                string sname = sh ? sh.name : "<null>";

                // 1) Shader bị strip/null → ưu tiên Unlit
                if (sh == null)
                {
                    if (preferUnlit && unlit) m.shader = unlit;
                    else if (pbr)             m.shader = pbr;
                    else if (std)             m.shader = std;
                    changed = true;
                }
                else
                {
                    bool isGlTF = sname.StartsWith("glTF") || sname.StartsWith("gltf") || sname.StartsWith("gITF");
                    bool isUnlit = sname.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isPbr   = sname.IndexOf("Pbr",   StringComparison.OrdinalIgnoreCase) >= 0;

                    // 2) Nếu đang PBR nhưng thích Unlit → ép về Unlit
                    if (preferUnlit && isPbr && unlit)
                    {
                        m.shader = unlit;
                        changed = true;
                    }
                    // 3) Shader lạ (không glTF/*) → fallback Unlit trước rồi Standard
                    else if (!isGlTF)
                    {
                        if (preferUnlit && unlit) m.shader = unlit;
                        else if (pbr)             m.shader = pbr;
                        else if (std)             m.shader = std;
                        changed = true;
                    }
                    // (Nếu đang Unlit rồi mà preferUnlit=true thì giữ nguyên)
                }

                // Remap vài property để nhìn ổn
                TryRemapCommonProperties(m);
                EnableEmissionIfPresent(m);
                m.enableInstancing = true;
            }

            if (changed) r.materials = mats;
        }
    }

    static void TryRemapCommonProperties(Material m)
    {
        // Base Color map
        var baseMap = GetFirstTex(m, "_BaseMap", "_BaseColorTexture", "_MainTex", "_BaseTex", "_BaseColor_Map");
        if (baseMap)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", baseMap);
            else if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", baseMap);
        }

        // Base Color tint
        var col = GetFirstColor(m, "_BaseColor", "_Color");
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        else if (m.HasProperty("_Color")) m.SetColor("_Color", col);

        // Normal
        var normalMap = GetFirstTex(m, "_NormalMap", "_BumpMap", "_NormalTexture", "_Normal_Map");
        if (normalMap)
        {
            if (m.HasProperty("_NormalMap")) m.SetTexture("_NormalMap", normalMap);
            else if (m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normalMap);
                m.EnableKeyword("_NORMALMAP");
            }
        }

        // Occlusion
        var occMap = GetFirstTex(m, "_OcclusionMap", "_OcclusionTexture", "_Occlusion_Map");
        if (occMap && m.HasProperty("_OcclusionMap")) m.SetTexture("_OcclusionMap", occMap);

        // Metallic/Smoothness (đơn giản)
        if (m.HasProperty("_Metallic"))
        {
            float metallic = GetFirstFloat(m, "_MetallicFactor", "_Metallic", 0.0f);
            m.SetFloat("_Metallic", metallic);
        }
        if (m.HasProperty("_Smoothness"))
        {
            float rough = GetFirstFloat(m, "_RoughnessFactor", 0.5f);
            m.SetFloat("_Smoothness", 1f - rough);
        }
    }

    static void EnableEmissionIfPresent(Material m)
    {
        var emisTex = GetFirstTex(m, "_EmissionMap", "_EmissiveTexture", "_Emission_Map");
        var emisCol = GetFirstColor(m, "_EmissionColor", "_EmissiveColor");
        bool hasEmis = (emisTex != null) || (emisCol.maxColorComponent > 0.0001f);

        if (hasEmis)
        {
            if (m.HasProperty("_EmissionMap") && emisTex) m.SetTexture("_EmissionMap", emisTex);
            if (m.HasProperty("_EmissionColor"))          m.SetColor("_EmissionColor", emisCol);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags &= ~MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
    }

    static Texture GetFirstTex(Material m, params string[] names)
    {
        foreach (var n in names)
            if (m.HasProperty(n))
            {
                var t = m.GetTexture(n);
                if (t) return t;
            }
        return null;
    }

    static Color GetFirstColor(Material m, params string[] names)
    {
        foreach (var n in names)
            if (m.HasProperty(n)) return m.GetColor(n);
        return Color.white;
    }

    static float GetFirstFloat(Material m, params object[] namesOrDefault)
    {
        float def = 0f;
        if (namesOrDefault.Length > 0 && namesOrDefault[^1] is float fdef)
        {
            def = fdef;
            Array.Resize(ref namesOrDefault, namesOrDefault.Length - 1);
        }
        foreach (var o in namesOrDefault)
        {
            if (o is string n && !string.IsNullOrEmpty(n) && m.HasProperty(n))
                return m.GetFloat(n);
        }
        return def;
    }
}
