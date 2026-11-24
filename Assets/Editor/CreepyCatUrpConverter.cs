using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CreepyCatTools
{
    /// <summary>
    /// 把 _Creepy_Cat 科幻包里的“内置渲染管线”材质批量转换为 URP 材质。
    ///
    /// 背景：这个资产包里的 .mat 全部使用 Built-in 的 Standard 等着色器，
    /// 直接导入 URP 工程会渲染成粉紫色（预制体缩略图看起来就像法线贴图）。
    /// 本脚本会保留 Albedo / 法线 / 金属度 / AO / 自发光 / 透明模式，只替换着色器。
    ///
    /// 使用：菜单 Tools → Creepy Cat → 1) 转材质 → 3) 刷新缩略图
    /// 无界面批量：Unity.exe -batchmode -quit -projectPath &lt;工程&gt;
    ///            -executeMethod CreepyCatTools.CreepyCatUrpConverter.ConvertBatch
    /// </summary>
    public static class CreepyCatUrpConverter
    {
        private const string PackRoot = "Assets/_Creepy_Cat";

        private const string UrpLit = "Universal Render Pipeline/Lit";
        private const string UrpUnlit = "Universal Render Pipeline/Unlit";
        private const string UrpParticlesLit = "Universal Render Pipeline/Particles/Lit";
        private const string UrpParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";

        // 这些着色器的材质不做转换（天空盒、UI、精灵等不是模型材质）
        private static readonly string[] SkipPrefixes =
        {
            "Skybox/", "UI/", "Sprites/", "GUI/", "Hidden/", "TextMeshPro/",
            "Universal Render Pipeline/", "HDRP/", "Shader Graphs/"
        };

        private static readonly HashSet<string> CheckedNormalMaps = new HashSet<string>();

        // ---------------------------------------------------------------- 菜单

        [MenuItem("Tools/Creepy Cat/1) 把 _Creepy_Cat 材质转换为 URP", false, 1)]
        public static void ConvertPackMaterials()
        {
            var paths = AssetDatabase.FindAssets("t:Material", new[] { PackRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();

            ConvertMaterials(paths);
        }

        [MenuItem("Tools/Creepy Cat/2) 把选中的材质转换为 URP", false, 2)]
        public static void ConvertSelectedMaterials()
        {
            var paths = Selection.objects
                .OfType<Material>()
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Creepy Cat", "请先在 Project 窗口中选中至少一个材质（.mat）。", "好");
                return;
            }

            ConvertMaterials(paths);
        }

        [MenuItem("Tools/Creepy Cat/3) 刷新 _Creepy_Cat 预制体缩略图", false, 3)]
        public static void RefreshPrefabPreviews()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { PackRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("刷新预制体缩略图", paths[i], (float)i / Mathf.Max(1, paths.Count));
                    AssetDatabase.ImportAsset(paths[i], ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            Debug.Log("[CreepyCat] 已强制重新导入 " + paths.Count + " 个预制体，缩略图会重新生成。");
        }

        [MenuItem("Tools/Creepy Cat/4) 一键修复（转材质 + 刷新缩略图）", false, 4)]
        public static void FixEverything()
        {
            ConvertPackMaterials();
            RefreshPrefabPreviews();
        }

        /// <summary>供 -executeMethod 使用的无界面入口。</summary>
        public static void ConvertBatch()
        {
            ConvertPackMaterials();
            RefreshPrefabPreviews();
        }

        // ---------------------------------------------------------------- 主流程

        private static void ConvertMaterials(List<string> paths)
        {
            if (Shader.Find(UrpLit) == null)
            {
                EditorUtility.DisplayDialog("Creepy Cat",
                    "找不到 Universal Render Pipeline/Lit 着色器。\n请确认工程已安装 URP 并把 URP 资源指定给 Graphics Settings。",
                    "好");
                return;
            }

            // 先把材质全部加载好，再统一修改（避免 StartAssetEditing 期间加载失败）
            var materials = new List<Material>();
            foreach (var path in paths)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) materials.Add(mat);
            }

            var converted = new List<string>();
            var skipped = new List<string>();
            var failed = new List<string>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var mat in materials)
                {
                    if (mat == null || mat.shader == null)
                    {
                        skipped.Add(mat == null ? "<null>" : "(空着色器)");
                        continue;
                    }

                    var shaderName = mat.shader.name;
                    if (SkipPrefixes.Any(p => shaderName.StartsWith(p)))
                    {
                        skipped.Add(mat.name + "  (" + shaderName + ")");
                        continue;
                    }

                    if (TryConvert(mat, shaderName, out var targetName))
                        converted.Add(mat.name + "   " + shaderName + "  →  " + targetName);
                    else
                        failed.Add(mat.name + "  (" + shaderName + ")");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            foreach (var line in converted) Debug.Log("[CreepyCat] 已转换：" + line);
            foreach (var line in failed) Debug.LogWarning("[CreepyCat] 转换失败：" + line);

            var message = "转换完成。\n\n成功：" + converted.Count +
                          "\n跳过：" + skipped.Count +
                          "\n失败：" + failed.Count +
                          "\n\n如果预制体缩略图仍是紫色，请执行菜单 3) 刷新缩略图。";

            Debug.Log("[CreepyCat] " + message.Replace("\n", " "));
            EditorUtility.DisplayDialog("Creepy Cat", message, "好");
        }

        private static bool TryConvert(Material mat, string shaderName, out string targetName)
        {
            targetName = ResolveTargetShader(shaderName);
            var target = Shader.Find(targetName);
            if (target == null)
            {
                targetName = "(找不到 " + targetName + ")";
                return false;
            }

            // ---------- 1. 读取旧材质的全部属性（切换着色器之前必须读完）----------
            var mainTex = GetTexture(mat, "_MainTex");
            var mainScale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
            var mainOffset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
            var color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

            var bumpMap = GetTexture(mat, "_BumpMap");
            var bumpScale = GetFloat(mat, "_BumpScale", 1f);

            var metallicGloss = GetTexture(mat, "_MetallicGlossMap");
            var specGloss = GetTexture(mat, "_SpecGlossMap");
            var parallaxMap = GetTexture(mat, "_ParallaxMap");
            var occlusionMap = GetTexture(mat, "_OcclusionMap");
            var occlusionStrength = GetFloat(mat, "_OcclusionStrength", 1f);

            var emissionMap = GetTexture(mat, "_EmissionMap");
            var emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
            var emissionOn = mat.IsKeywordEnabled("_EMISSION") || emissionColor.maxColorComponent > 0.002f;

            var metallic = GetFloat(mat, "_Metallic", 0f);
            var smoothness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : GetFloat(mat, "_Smoothness", 0.5f);
            var cutoff = GetFloat(mat, "_Cutoff", 0.5f);
            var mode = mat.HasProperty("_Mode") ? Mathf.RoundToInt(mat.GetFloat("_Mode")) : 0;
            var cull = mat.HasProperty("_CullMode") ? mat.GetFloat("_CullMode") : GetFloat(mat, "_Cull", 2f);
            var specularWorkflow = shaderName.Contains("Specular");

            // ---------- 2. 换着色器并恢复属性 ----------
            Undo.RecordObject(mat, "转换为 URP 材质");
            mat.shader = target;

            // 基础贴图 / 颜色
            if (mainTex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", mainTex);
                mat.SetTextureScale("_BaseMap", mainScale);
                mat.SetTextureOffset("_BaseMap", mainOffset);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

            // 法线
            if (bumpMap != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", bumpMap);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", bumpScale);
                mat.EnableKeyword("_NORMALMAP");
                EnsureNormalMapImport(bumpMap);
            }

            // 视差（如果旧材质有）
            if (parallaxMap != null && mat.HasProperty("_ParallaxMap"))
            {
                mat.SetTexture("_ParallaxMap", parallaxMap);
                mat.EnableKeyword("_PARALLAXMAP");
            }

            // 金属度 / 镜面工作流
            if (mat.HasProperty("_WorkflowMode")) mat.SetFloat("_WorkflowMode", specularWorkflow ? 0f : 1f);
            if (specularWorkflow && specGloss != null && mat.HasProperty("_SpecGlossMap"))
            {
                mat.SetTexture("_SpecGlossMap", specGloss);
                mat.EnableKeyword("_SPECULAR_SETUP");
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            else if (metallicGloss != null && mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", metallicGloss);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

            // 环境光遮蔽
            if (occlusionMap != null && mat.HasProperty("_OcclusionMap"))
            {
                mat.SetTexture("_OcclusionMap", occlusionMap);
                if (mat.HasProperty("_OcclusionStrength")) mat.SetFloat("_OcclusionStrength", occlusionStrength);
            }

            // 自发光
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionOn ? emissionColor : Color.black);
            if (emissionMap != null && mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emissionMap);
            if (emissionOn) mat.EnableKeyword("_EMISSION");
            else mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = emissionOn
                ? MaterialGlobalIlluminationFlags.RealtimeEmissive
                : MaterialGlobalIlluminationFlags.None;

            // 不透明 / 消融 / 透明
            ApplySurfaceType(mat, mode, cutoff, cull);

            mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(mat);
            return true;
        }

        private static string ResolveTargetShader(string shaderName)
        {
            if (shaderName.StartsWith("Particles/"))
                return shaderName.Contains("Unlit") ? UrpParticlesUnlit : UrpParticlesLit;

            if (shaderName.StartsWith("Unlit/"))
                return UrpUnlit;

            // Standard / Standard (Specular setup) / Legacy Shaders/...
            return UrpLit;
        }

        private static void ApplySurfaceType(Material mat, int standardMode, float cutoff, float cull)
        {
            // 只有 URP 的模型着色器才有 _Surface
            if (!mat.HasProperty("_Surface")) return;

            // Built-in Standard 的 _Mode：0=Opaque，1=Cutout，2=Fade，3=Transparent
            var transparent = standardMode >= 2;
            var cutout = standardMode == 1;

            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);      // 0 = Alpha 混合
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", cull);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", cutoff);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);

            if (transparent)
            {
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (cutout) mat.EnableKeyword("_ALPHATEST_ON");
                else mat.DisableKeyword("_ALPHATEST_ON");
                mat.renderQueue = cutout ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
            }
        }

        /// <summary>把法线贴图的导入类型纠正为 Normal Map（否则 URP 下光照会怪）。</summary>
        private static void EnsureNormalMapImport(Texture texture)
        {
            if (texture == null) return;

            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !CheckedNormalMaps.Add(path)) return;

            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == TextureImporterType.NormalMap) return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- 小工具

        private static Texture GetTexture(Material mat, string property)
        {
            return mat.HasProperty(property) ? mat.GetTexture(property) : null;
        }

        private static float GetFloat(Material mat, string property, float fallback)
        {
            return mat.HasProperty(property) ? mat.GetFloat(property) : fallback;
        }
    }
}
