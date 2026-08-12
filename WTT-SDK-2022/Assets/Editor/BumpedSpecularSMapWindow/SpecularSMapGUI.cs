// BumpedSpecularSMapGUI.cs
// Shared property-drawing logic for materials using "p0/Reflective/Bumped Specular SMap".
// Used by both BumpedSpecularSMapEditor (custom Inspector) and BumpedSpecularSMapWindow (floating window),
// so the two stay in sync automatically instead of drifting into two separate UIs.
//
// Place under an "Editor" folder anywhere in your project's Assets.

using UnityEditor;
using UnityEngine;

namespace Editor.BumpedSpecularSMapWindow
{
    public static class SpecularSMapGUI
    {
        public const string ShaderName = "p0/Reflective/Bumped Specular SMap";

        private static bool _showWetting = true;
        private static bool _showHeat = true;
        private static bool _showAdvanced = false;

        /// <summary>
        /// Draws the full property UI for a single material. Returns true if anything changed.
        /// materialEditor may be null when called from a plain EditorWindow (not an Inspector) —
        /// in that case texture fields and previews fall back to plain EditorGUILayout controls.
        /// </summary>
        public static bool Draw(Material mat, MaterialEditor materialEditor)
        {
            if (mat == null) return false;

            EditorGUI.BeginChangeCheck();

            bool useScene = DrawToggle(mat, "_SceneLight", "USESCENELIGHT", "Switch Out Shader behaviour");
            EditorGUILayout.LabelField("Base", EditorStyles.boldLabel);
            DrawColor(mat, "_Color", "Main Color");
            DrawColor(mat, "_BaseTintColor", "Tint Color");
            DrawTexture(mat, materialEditor, "_MainTex", "Base (RGB) Specular (A)");

            bool hasTint = DrawToggle(mat, "_HasTint", "TINTMASK", "Has Tint");
            using (new EditorGUI.DisabledScope(!hasTint))
            {
                EditorGUI.indentLevel++;
                DrawTexture(mat, materialEditor, "_TintMask", "Tint Mask");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Specular / Reflection", EditorStyles.boldLabel);
            DrawTexture(mat, materialEditor, "_SpecMap", "Gloss Map");
            DrawColor(mat, "_SpecColor", "Specular Color");
            DrawRange(mat, "_Glossness", "Specularness", 0.01f, 10f);
            DrawRange(mat, "_Specularness", "Glossness", 0.01f, 10f);
            DrawColor(mat, "_ReflectColor", "Reflection Color");
            DrawTexture(mat, materialEditor, "_Cube", "Reflection Cubemap");
            DrawVector4(mat, "_SpecVals", "Specular Vals (offset, scale, -, -)");
            DrawVector4(mat, "_DefVals", "Diffuse Vals (offset, scale, -, -)");
            DrawFloat(mat, "_DropsSpec", "Drops Spec");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Normal Map", EditorStyles.boldLabel);
            DrawTexture(mat, materialEditor, "_BumpMap", "Normal Map");
            DrawFloat(mat, "_BumpTiling", "Bump Tiling");
            DrawFloat(mat, "_NormalIntensity", "Normal Intensity");
            DrawFloat(mat, "_NormalUVMultiplier", "Normal UV Tiling");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Depth Offset", EditorStyles.boldLabel);
            DrawFloat(mat, "_Factor", "Z Offset Angle");
            DrawFloat(mat, "_Units", "Z Offset Forward");

            EditorGUILayout.Space();
            _showWetting = EditorGUILayout.Foldout(_showWetting, "Wetting", true);
            if (_showWetting)
            {
                EditorGUI.indentLevel++;
                DrawFloat(mat, "_RippleTexScale", "Ripple Tex Scale");
                DrawFloat(mat, "_RippleFakeLightIntensityOffset", "Ripple Fake Light Offset");
                DrawFloat(mat, "_NightRippleFakeLightOffset", "Night Fake Light Offset");
                DrawFloat(mat, "_NdotLOffset", "Normal Dot Light Offset");
                DrawToggle(mat, "_USERAIN", "USERAIN", "Affected By Rain");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _showHeat = EditorGUILayout.Foldout(_showHeat, "Heat / Thermal", true);
            if (_showHeat)
            {
                EditorGUI.indentLevel++;
                bool useHeat = DrawToggle(mat, "USEHEAT", "USEHEAT", "Use Metal Heat Glow");
                using (new EditorGUI.DisabledScope(!useHeat))
                {
                    DrawFloat(mat, "_HeatVisible", "Heat Visible (0-1, thermal vision only)");
                    DrawFloat(mat, "_HeatTemp", "Heat Temp");
                }

                EditorGUILayout.Space(4);
                DrawHDRColor(mat, "_HeatColor1", "Heat Color 1 (inside box)");
                DrawHDRColor(mat, "_HeatColor2", "Heat Color 2 (box wall / edge)");
                DrawVector4(mat, "_HeatCenter", "Heat Center (box center, object-space)");
                DrawVector4(mat, "_HeatSize", "Heat Size (box full extent, like BoxCollider.size)");

                EditorGUILayout.Space(4);
                bool visualizeHeat = DrawToggle(mat, "_VisualizeHeat", "VISUALIZEHEAT",
                    "Visualize Heat (blend Heat Color 1/2 gradient onto surface)");
                using (new EditorGUI.DisabledScope(!visualizeHeat))
                {
                    DrawRange(mat, "_HeatBlendStrength", "Visualize Heat Blend Strength", 0f, 1f);
                }
                if (visualizeHeat)
                {
                    EditorGUILayout.HelpBox(
                        "Independent of 'Use Metal Heat Glow' above - this blends the Heat Color " +
                        "gradient onto the surface only inside the box defined by Heat Center/Heat " +
                        "Size (Heat Size is a full extent, same convention as a BoxCollider). Solid " +
                        "Heat Color 1 fills the box interior, gradients to Heat Color 2 near the " +
                        "walls, and the surface is left completely untouched outside the box.",
                        MessageType.Info);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced / Internal", true);
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                DrawStencilType(mat);
                DrawFloat(mat, "_SkinnedMeshMaterial", "Skinned Mesh Material (internal)");
                if (HasProperty(mat, "_DebugView"))
                    DrawFloat(mat, "_DebugView", "Debug View (diagnostic)");
                EditorGUI.indentLevel--;
            }

            if (materialEditor != null)
            {
                EditorGUILayout.Space();
                materialEditor.RenderQueueField();
                materialEditor.EnableInstancingField();
            }

            return EditorGUI.EndChangeCheck();
        }

        public static bool IsCompatible(Material mat)
        {
            return mat != null && mat.shader != null && mat.shader.name == ShaderName;
        }

        // ---- helpers -------------------------------------------------------

        private static bool HasProperty(Material mat, string name) => mat.HasProperty(name);

        private static void DrawColor(Material mat, string prop, string label)
        {
            if (!mat.HasProperty(prop)) return;
            Color c = EditorGUILayout.ColorField(new GUIContent(label), mat.GetColor(prop));
            mat.SetColor(prop, c);
        }

        private static void DrawHDRColor(Material mat, string prop, string label)
        {
            if (!mat.HasProperty(prop)) return;
            Color c = EditorGUILayout.ColorField(new GUIContent(label), mat.GetColor(prop), true, true, true);
            mat.SetColor(prop, c);
        }

        private static void DrawFloat(Material mat, string prop, string label)
        {
            if (!mat.HasProperty(prop)) return;
            float v = EditorGUILayout.FloatField(label, mat.GetFloat(prop));
            mat.SetFloat(prop, v);
        }

        private static void DrawRange(Material mat, string prop, string label, float min, float max)
        {
            if (!mat.HasProperty(prop)) return;
            float v = EditorGUILayout.Slider(label, mat.GetFloat(prop), min, max);
            mat.SetFloat(prop, v);
        }

        private static void DrawVector4(Material mat, string prop, string label)
        {
            if (!mat.HasProperty(prop)) return;
            Vector4 v = EditorGUILayout.Vector4Field(label, mat.GetVector(prop));
            mat.SetVector(prop, v);
        }

        private static void DrawTexture(Material mat, MaterialEditor materialEditor, string prop, string label)
        {
            if (!mat.HasProperty(prop)) return;

            if (materialEditor != null)
            {
                // Use Unity's built-in texture slot UI (with drag-drop, tiling/offset foldout, preview).
                MaterialProperty mp = MaterialEditor.GetMaterialProperty(new Object[] { mat }, prop);
                materialEditor.TexturePropertySingleLine(new GUIContent(label), mp);
            }
            else
            {
                // Plain fallback for the floating window (no MaterialEditor instance).
                Texture t = mat.GetTexture(prop);
                Texture newT = (Texture)EditorGUILayout.ObjectField(label, t, typeof(Texture), false);
                if (newT != t) mat.SetTexture(prop, newT);
            }
        }

        private static bool DrawToggle(Material mat, string prop, string keyword, string label)
        {
            if (!mat.HasProperty(prop)) return false;
            bool value = mat.GetFloat(prop) > 0.5f;
            bool newValue = EditorGUILayout.Toggle(label, value);
            if (newValue != value)
            {
                mat.SetFloat(prop, newValue ? 1f : 0f);
                if (!string.IsNullOrEmpty(keyword))
                {
                    if (newValue) mat.EnableKeyword(keyword);
                    else mat.DisableKeyword(keyword);
                }
            }
            return newValue;
        }

        private static readonly string[] StencilTypeNames = { "Static", "Characters", "Hands" };

        private static void DrawStencilType(Material mat)
        {
            const string prop = "_StencilType";
            if (!mat.HasProperty(prop)) return;
            int current = Mathf.Clamp((int)mat.GetFloat(prop), 0, StencilTypeNames.Length - 1);
            int selected = EditorGUILayout.Popup("Stencil Type", current, StencilTypeNames);
            if (selected != current) mat.SetFloat(prop, selected);
        }
    }
}
