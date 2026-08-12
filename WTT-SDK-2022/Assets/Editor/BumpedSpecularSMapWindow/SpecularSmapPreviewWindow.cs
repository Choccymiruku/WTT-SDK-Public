using System;
using UnityEditor;
using UnityEngine;

namespace Editor.BumpedSpecularSMapWindow
{
    internal sealed class SpecularSmapPreviewWindow : EditorWindow
    {
        private SpecularSMapWindow _owner;

        public static SpecularSmapPreviewWindow Open(SpecularSMapWindow owner)
        {
            var window = CreateInstance<SpecularSmapPreviewWindow>();
            window.titleContent = new GUIContent("Model Preview");
            window._owner = owner;
            window.Show();
            return window;
        }

        private void OnGUI()
        {
            if (_owner == null)
            {
                EditorGUILayout.HelpBox("Material Editor window was closed.", MessageType.Warning);
                return;
            }
            _owner.CreatePreview();
        }

        private void Update()
        {
            Repaint();
        }

        private void OnDestroy()
        {
            _owner?.NotifyPreviewWindowClosed();
        }
    }
}