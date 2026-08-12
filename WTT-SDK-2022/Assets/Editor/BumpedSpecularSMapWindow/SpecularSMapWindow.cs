using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Editor.BumpedSpecularSMapWindow
{
    [Serializable]
    public class BackgroundOption
    {
        public string Name;

        public BackgroundOption(string name)
        {
            Name = name;
        }
    }

    public class SpecularSMapWindow : EditorWindow
    {
        [SerializeField] public GameObject _objectPrefab;
        [SerializeField] public GameObject _LightsEnvironment;

        // Expandable list of named background options. Starts with the two requested
        // presets; add more entries here (or expose an Add/Remove button later) and
        // they'll appear in the "Background" dropdown automatically. Each option's actual
        // image is NOT assigned manually - it's looked up in _backgroundFolder by filename
        // matching the option Name (see ResolveBackgroundTexture / DrawBackgroundField).
        [SerializeField] private List<BackgroundOption> _backgroundOptions = new List<BackgroundOption>
        {
            new BackgroundOption("None"),
            new BackgroundOption("Item Preview"),
            new BackgroundOption("Build View"),
        };
        [SerializeField] private int _backgroundIndex = 0;

        // Fixed folder searched for an image whose filename matches the selected option's Name.
        private const string BackgroundFolderPath = "Assets/Editor/BumpedSpecularSMapWindow/Backgrounds";

        // Small cache so we're not hitting AssetDatabase.FindAssets on every OnGUI call -
        // invalidated whenever the folder or option list changes.
        private readonly Dictionary<string, Texture2D> _backgroundCache = new Dictionary<string, Texture2D>();
        private string _backgroundCacheFolderPath;

        private static readonly string[] BackgroundImageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".psd" };

        private string[] _materialName = Array.Empty<string>();
        private Material[] _prefabMaterials = Array.Empty<Material>();
        private Material _objectMaterial;
        private int _materialNameIndex;
        private Vector2 _scroll;
        private SpecularSmapPreviewControl _previewControl;
        private SpecularSmapPreviewWindow _previewWindow;

        [MenuItem("Custom Windows/Specular SMap Editor")]
        public static void Open()
        {
            var window = GetWindow<SpecularSMapWindow>();
            window.titleContent = new GUIContent("Specular SMap Editor");
            window.Show();
        }
        
        private void OnEnable()
        {
            _previewControl = new SpecularSmapPreviewControl();
            _previewControl.Initialize();

            // Needed so MouseMove events actually reach OnGUI, which is how the timeline
            // boxes know to expand on hover without requiring a click.
            wantsMouseMove = true;
        }

        private void OnDisable()
        {
            _previewControl?.Dispose();
            _previewControl = null;
        }

        private void OnGUI()
        {
            DrawHeader();
            GUILayout.Label("Input Field", EditorStyles.boldLabel);
            EditorGUILayout.Space(width: 4);

            DrawBackgroundField();

            _objectPrefab = (GameObject)EditorGUILayout.ObjectField("Weapon Prefab", _objectPrefab, typeof(GameObject), true);
            _LightsEnvironment = (GameObject)EditorGUILayout.ObjectField("Environment Lightning", _LightsEnvironment, typeof(GameObject), true);
            if (_objectPrefab == null)
            {
                EditorGUILayout.HelpBox("Please assign an Prefab object.", MessageType.Warning);
                return;
            }
            GetMaterials();
            string[] matNames = _materialName;
            DrawSearchableDropdownField("Materials", matNames, _materialNameIndex, selected =>
            {
                _materialNameIndex = selected;
                _objectMaterial = _prefabMaterials[selected];
            }, true,  "Select Materials You want to Modify");
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_objectMaterial == null)
            {
                EditorGUILayout.HelpBox("No material selected.", MessageType.Info);
            }
            else if (SpecularSMapGUI.IsCompatible(_objectMaterial))
            {
                SpecularSMapGUI.Draw(_objectMaterial, null);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Selected material uses shader \"" + _objectMaterial.shader.name + "\", " +
                    "not \"" + SpecularSMapGUI.ShaderName + "\".\n\n" +
                    "This editor is only guaranteed to make sense for the latter.",
                    MessageType.Warning);
            }
            EditorGUILayout.EndScrollView();
            
            //Preview Window stuff
            GUILayout.Space(10);
            
            if (_previewWindow != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("Preview is open in a separate window.", MessageType.Info);
                    if (GUILayout.Button("Reattach", GUILayout.Width(90)))
                    {
                        DockPreviewWindow();
                        Repaint();
                    }
                } 
            }
            else
            {
                CreatePreview();
            }
        }
        
        private void DrawBackgroundField()
        {
            string[] names = _backgroundOptions.Select(o => o.Name).ToArray();
            _backgroundIndex = Mathf.Clamp(_backgroundIndex, 0, Mathf.Max(names.Length - 1, 0));

            DrawSearchableDropdownField("Background", names, _backgroundIndex, selected =>
            {
                _backgroundIndex = selected;
            }, true, "Background image shown behind the preview object");

            if (_backgroundIndex == 0) return;
            if (_backgroundOptions.Count == 0) return;

            string optionName = _backgroundOptions[_backgroundIndex].Name;
            Texture2D resolved = ResolveBackgroundTexture(optionName);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("  Resolved Image", resolved, typeof(Texture2D), false);
            }

            if (resolved == null)
            {
                EditorGUILayout.HelpBox(
                    $"No image named \"{optionName}\" (png/jpg/jpeg/tga/psd) found in " +
                    $"\"{BackgroundFolderPath}\".",
                    MessageType.Warning);
            }
        }

        /// <summary>
        /// Finds a Texture2D asset directly inside _backgroundFolder whose filename (without
        /// extension) exactly matches optionName. Results are cached per (folder, optionName)
        /// until the folder is reassigned or the option list changes.
        /// </summary>
        private Texture2D ResolveBackgroundTexture(string optionName)
        {
            string folderPath = BackgroundFolderPath;

            if (_backgroundCacheFolderPath != folderPath)
            {
                _backgroundCache.Clear();
                _backgroundCacheFolderPath = folderPath;
            }

            if (_backgroundCache.TryGetValue(optionName, out var cached))
                return cached; // may be null - a prior miss is cached too, to avoid re-searching every OnGUI

            Texture2D found = null;
            foreach (string ext in BackgroundImageExtensions)
            {
                string candidatePath = $"{folderPath}/{optionName}{ext}";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(candidatePath);
                if (tex != null)
                {
                    found = tex;
                    break;
                }
            }

            _backgroundCache[optionName] = found;
            return found;
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Bumped Specular SMap Material Editor", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSearchableDropdownField(string label, string[] options, int currentIndex, Action<int> onSelected, bool isTooltip, [CanBeNull] string description)
        {
            Rect lineRect = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(lineRect.x, lineRect.y, EditorGUIUtility.labelWidth, lineRect.height);
            Rect fieldRect = new Rect(lineRect.x + EditorGUIUtility.labelWidth, lineRect.y, lineRect.width - EditorGUIUtility.labelWidth, lineRect.height);

            EditorGUI.LabelField(labelRect, label);

            string currentLabel = options.Length > 0 && currentIndex >= 0 && currentIndex < options.Length ? options[currentIndex] : "-";

            if (isTooltip)
            {
                if (EditorGUI.DropdownButton(fieldRect, new GUIContent(currentLabel, description), FocusType.Keyboard))
                {
                    var dropdown = new SearchableStringDropdown(label, options, onSelected);
                    dropdown.Show(fieldRect);
                }
            }
            else
            {
                if (EditorGUI.DropdownButton(fieldRect, new GUIContent(currentLabel), FocusType.Keyboard))
                {
                    var dropdown = new SearchableStringDropdown(label, options, onSelected);
                    dropdown.Show(fieldRect);
                }
            }
        }

        private void GetMaterials()
        {
            _materialName = Array.Empty<string>();
            _prefabMaterials = Array.Empty<Material>();

            MeshRenderer[] meshRenderers =
                _objectPrefab.GetComponentsInChildren<MeshRenderer>(true);

            List<Material> materials = new List<Material>();

            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                foreach (Material material in meshRenderer.sharedMaterials)
                {
                    if (material == null)
                        continue;

                    if (!materials.Contains(material))
                        materials.Add(material);
                }
            }

            _prefabMaterials = materials.ToArray();

            _materialName = new string[_prefabMaterials.Length];

            for (int i = 0; i < _prefabMaterials.Length; i++)
            {
                _materialName[i] = _prefabMaterials[i].name;
            }

            // Keep the current selection valid (and the material reference in sync) as the
            // prefab changes out from under the dropdown - otherwise _objectMaterial can end
            // up pointing at a material from a prefab that's no longer assigned.
            if (_prefabMaterials.Length == 0)
            {
                _objectMaterial = null;
                _materialNameIndex = 0;
            }
            else
            {
                _materialNameIndex = Mathf.Clamp(_materialNameIndex, 0, _prefabMaterials.Length - 1);
                _objectMaterial = _prefabMaterials[_materialNameIndex];
            }
        }

        internal void CreatePreview()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Preview Window", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                // Only offer to pop out from the main window - the popped-out window itself
                // doesn't need a way to pop out again.
                if (_previewWindow == null && GUILayout.Button("Detach", GUILayout.Width(70)))
                {
                    PopOutPreviewWindow();
                    Repaint();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                Vector3 newOffset = EditorGUILayout.Vector3Field("Pivot Offset", _previewControl.PivotOffset);
                if (EditorGUI.EndChangeCheck())
                    _previewControl.PivotOffset = newOffset;

                if (GUILayout.Button("Recenter", GUILayout.Width(70)))
                    _previewControl.RecenterPivot();
            }

            Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            // Build/rebuild BEFORE rendering, so a prefab or lighting-environment swap this
            // frame shows up immediately instead of lagging one repaint behind.
            _previewControl.EnsurePreviewObject(_objectPrefab, _LightsEnvironment);
            
            Texture2D backgroundImage = (_backgroundOptions.Count > 0)
                ? ResolveBackgroundTexture(_backgroundOptions[_backgroundIndex].Name)
                : null;

            if (_backgroundIndex == 0)
            {
                backgroundImage = null;
            }
            _previewControl.Render(previewRect, backgroundImage);
        }
        
        private void PopOutPreviewWindow()
        {
            if (_previewWindow != null)
            {
                _previewWindow.Focus();
                return;
            }

            _previewWindow = SpecularSmapPreviewWindow.Open(this);
        }

        private void DockPreviewWindow()
        {
            if (_previewWindow != null)
            {
                _previewWindow.Close(); // triggers OnDestroy -> NotifyPreviewWindowClosed
            }
        }
        
        internal void NotifyPreviewWindowClosed()
        {
            _previewWindow = null;
            Repaint();
        }
    }
}