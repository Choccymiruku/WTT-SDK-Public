// BumpedSpecularSMapEditor.cs
// Custom Inspector for materials using "p0/Reflective/Bumped Specular SMap".
// Assign via the shader's `CustomEditor "P0.ShaderTools.BumpedSpecularSMapEditor"` line,
// or it will simply not be picked up automatically if the shader still points at its
// original CustomEditor name - see the note at the bottom of this file.
//
// Place under an "Editor" folder anywhere in your project's Assets.

using UnityEditor;
using UnityEngine;

namespace Editor.BumpedSpecularSMapWindow
{
    public class SpecularSMapSMapEditor : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            Material mat = materialEditor.target as Material;
            if (mat == null) return;

            EditorGUILayout.HelpBox(
                "Custom inspector for " + SpecularSMapGUI.ShaderName,
                MessageType.None);
            EditorGUILayout.Space();

            SpecularSMapGUI.Draw(mat, materialEditor);
        }
    }
}

// -----------------------------------------------------------------------------
// HOW THIS GETS APPLIED
// -----------------------------------------------------------------------------
// Unity picks the custom Inspector for a shader from the ShaderLab file itself:
//
//     CustomEditor "P0.ShaderTools.BumpedSpecularSMapEditor"
//
// The Bumped_Specular_SMap.shader we fixed currently ends with:
//
//     CustomEditor "FresnelMaterialEditor"
//
// Change that line to point at this class's *full* name (namespace included), e.g.:
//
//     CustomEditor "P0.ShaderTools.BumpedSpecularSMapEditor"
//
// If you'd rather keep the original FresnelMaterialEditor as the default and only use
// this one occasionally, you don't have to touch the shader at all — just use the
// floating "P0 > Bumped Specular SMap Editor" window instead (BumpedSpecularSMapWindow.cs),
// which works on any material regardless of which CustomEditor the shader declares.
// -----------------------------------------------------------------------------
