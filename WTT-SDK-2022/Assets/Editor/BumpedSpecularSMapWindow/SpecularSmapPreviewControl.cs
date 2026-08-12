using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Editor.BumpedSpecularSMapWindow
{
    internal sealed class SpecularSmapPreviewControl : IDisposable
    {
        private PreviewRenderUtility _renderUtility;
        private Material _alphaBlendMaterial;

        // What we last built the preview instances from - used to detect changes
        // so we know when to rebuild rather than reaching back into the owner window.
        private GameObject _builtFromPrefab;
        private GameObject _builtFromEnvironment;

        private GameObject _previewObject;
        private GameObject _previewEnvironment;

        // Object rotation - yaw/tilt turntable model (see Rotate()) instead of camera orbit.
        private float _currentYaw;
        private float _currentTilt;
        public float MinTilt = -30f;
        public float MaxTilt = 30f;
        private Quaternion _objectLocalRotation = Quaternion.identity;

        // Fixed viewing angle - the camera no longer orbits, it always looks from this
        // direction at whatever the current pivot/distance are.
        private readonly Quaternion _fixedViewAngle = Quaternion.Euler(0f, 90f, 0f);

        private float _distance = 0.3f;

        // Pivot = auto-detected mesh bounds center + a user-editable offset, so it defaults
        // to something sensible but can be moved anywhere.
        public Vector3 _autoBoundsCenter = Vector3.zero;
        public Vector3 PivotOffset = Vector3.zero;
        private Vector3 Pivot => PivotOffset;

        // Cached once per rebuilt subject: the "pivotPosition" value read off a top-level
        // PreviewPivot component, if the prefab has one. Looked up by type name via reflection
        // (see TryGetPreviewPivotOffset) rather than a direct reference, since PreviewPivot is a
        // runtime MonoBehaviour living outside this Editor assembly and may not always be
        // referenced/compiled alongside this tool.
        private bool _hasPreviewPivotOverride;
        private Vector3 _previewPivotOverrideValue;

        public void Initialize()
        {
            _renderUtility = new PreviewRenderUtility { cameraFieldOfView = 30f };
            _renderUtility.camera.nearClipPlane = 0.01f;
            _renderUtility.camera.farClipPlane = 1000f;
            _renderUtility.camera.renderingPath = RenderingPath.UsePlayerSettings;

            // NOTE: PreviewRenderUtility's internal render texture is opaque (no alpha channel)
            // on most Unity versions - EndAndDrawPreview() always blits an opaque result, so
            // setting backgroundColor's alpha to 0 does NOT make empty space transparent; it
            // still clears to solid black. That's why the background image was being fully
            // covered. We render to our OWN alpha-capable RenderTexture instead (see Render())
            // and composite it over the background ourselves with GUI.DrawTexture, which does
            // respect alpha blending.
            _renderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _renderUtility.camera.backgroundColor = new Color(0f, 0f, 0f, 0f);

            _renderUtility.lights[0].intensity = 1.1f;
            _renderUtility.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            _renderUtility.lights[1].intensity = 0.4f;
            _renderUtility.lights[1].transform.rotation = Quaternion.Euler(-30f, -120f, 0f);
        }

        public void Dispose()
        {
            _renderUtility?.Cleanup();

            if (_alphaBlendMaterial != null)
                UnityEngine.Object.DestroyImmediate(_alphaBlendMaterial);

            if (_previewEnvironment != null)
                UnityEngine.Object.DestroyImmediate(_previewEnvironment);

            if (_previewObject != null)
                UnityEngine.Object.DestroyImmediate(_previewObject);
        }

        /// <summary>
        /// Builds (or rebuilds) the preview instances if the given prefab/environment
        /// references differ from what's currently instantiated. Call every frame -
        /// it's a no-op when nothing has changed.
        /// </summary>
        public void EnsurePreviewObject(GameObject userPreviewPrefab, GameObject userLightningPrefab)
        {
            bool subjectChanged = userPreviewPrefab != _builtFromPrefab;
            bool environmentChanged = userLightningPrefab != _builtFromEnvironment;

            if (!subjectChanged && !environmentChanged)
                return;

            if (subjectChanged)
                RebuildSubject(userPreviewPrefab);

            if (environmentChanged)
                RebuildEnvironment(userLightningPrefab);
        }

        private void RebuildSubject(GameObject userPreviewPrefab)
        {
            if (_previewObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewObject);
                _previewObject = null;
            }

            _builtFromPrefab = userPreviewPrefab;

            if (userPreviewPrefab != null)
            {
                _previewObject = UnityEngine.Object.Instantiate(userPreviewPrefab);
            }
            else
            {
                // Fallback so the preview still shows *something* when no prefab is assigned.
                _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            }

            _previewObject.hideFlags = HideFlags.HideAndDontSave;
            _previewObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _renderUtility.AddSingleGO(_previewObject);

            // New subject -> recompute bounds center and reset rotation/pivot offset so the
            // new object starts framed and unrotated, rather than inheriting the last one's state.
            _autoBoundsCenter = ComputeBoundsCenter(_previewObject);
            //PivotOffset = _autoBoundsCenter;
            _currentYaw = 0f;
            _currentTilt = 0f;
            _objectLocalRotation = Quaternion.identity;
            _distance = Mathf.Max(ComputeBoundsRadius(_previewObject) * 2.2f, 0.5f);
            
            // Check for a PreviewPivot component on the instantiated object itself (top-level,
            // not children). If present, its pivotPosition overrides our default zero offset;
            // if absent, do nothing extra and fall back to the normal zeroed offset as before.
            _hasPreviewPivotOverride = TryGetPreviewPivotOffset(_previewObject, out _previewPivotOverrideValue);
            PivotOffset = _hasPreviewPivotOverride ? _previewPivotOverrideValue : _autoBoundsCenter;
        }

        private void RebuildEnvironment(GameObject userLightningPrefab)
        {
            if (_previewEnvironment != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewEnvironment);
                _previewEnvironment = null;
            }

            _builtFromEnvironment = userLightningPrefab;

            if (userLightningPrefab == null)
                return;

            _previewEnvironment = UnityEngine.Object.Instantiate(userLightningPrefab);
            _previewEnvironment.hideFlags = HideFlags.HideAndDontSave;
            _renderUtility.AddSingleGO(_previewEnvironment);

            // If the environment prefab brings its own lights, prefer those over the
            // PreviewRenderUtility default two-light rig instead of double-lighting the scene.
            if (_previewEnvironment.GetComponentInChildren<Light>() != null)
            {
                _renderUtility.lights[0].intensity = 0f;
                _renderUtility.lights[1].intensity = 0f;
            }
        }

        private static Vector3 ComputeBoundsCenter(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return go.transform.position;

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b.center;
        }

        private static float ComputeBoundsRadius(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;

            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return Mathf.Max(b.extents.magnitude, 0.05f);
        }

        /// <summary>Recomputes the auto pivot from the current subject's mesh bounds and clears the manual offset.</summary>
        public void RecenterPivot()
        {
            if (_previewObject == null) return;
            
            _hasPreviewPivotOverride = TryGetPreviewPivotOffset(_previewObject, out _previewPivotOverrideValue);
            if (_hasPreviewPivotOverride)
            {
                PivotOffset = _previewPivotOverrideValue;
            }
            else
            {
                _autoBoundsCenter = ComputeBoundsCenter(_previewObject);
                PivotOffset = _autoBoundsCenter;
            }
        }

        /// <summary>
        /// Looks for a component named exactly "PreviewPivot" directly on go (not children),
        /// and if found, reads its public "pivotPosition" Vector3 field or property via
        /// reflection. Using reflection here - rather than a direct type reference - means this
        /// Editor-only tool doesn't need an assembly reference to wherever the runtime
        /// PreviewPivot script lives, and simply no-ops (returns false) if the component or
        /// member isn't found, exactly as if it were absent.
        /// </summary>
        private static bool TryGetPreviewPivotOffset(GameObject go, out Vector3 pivotPosition)
        {
            pivotPosition = Vector3.zero;
 
            Component found = null;
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp != null && comp.GetType().Name == "PreviewPivot")
                {
                    found = comp;
                    break;
                }
            }

            if (found == null) return false;

            var type = found.GetType();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | BindingFlags.GetField;

            var field = type.GetField("pivotPosition", flags);
            if (field != null && field.FieldType == typeof(Vector3))
            {
                pivotPosition = (Vector3)field.GetValue(found);
                return true;
            }

            // Component named PreviewPivot exists but doesn't expose a Vector3 pivotPosition
            // member matching what was asked for - treat as absent rather than guessing.
            Debug.LogWarning(
                "SpecularSmapPreviewControl: found a PreviewPivot component but it has no " +
                "public Vector3 field/property named 'pivotPosition' - ignoring it.");
            return false;
        }

        /// <summary>
        /// Turntable-style rotation: yaw wraps freely, tilt is clamped to [minTilt, maxTilt],
        /// and the tilt delta's sign flips depending on which side of the yaw circle the object
        /// is currently facing (so dragging "up" always visually tilts the same way regardless
        /// of how far the object has been spun around).
        /// </summary>
        public void Rotate(float angle, float tilt = 0f, float minTilt = -30f, float maxTilt = 30f)
        {
            if (_previewObject == null)
            {
                return;
            }
            _currentYaw -= angle * 0.3f;
            if (_currentYaw >= 360f)
            {
                _currentYaw -= 360f;
            }
            else if (_currentYaw < -360f)
            {
                _currentYaw += 360f;
            }
            int sign = (Mathf.Cos(0.017453292f * _currentYaw) >= 0f) ? 1 : (-1);
            _currentTilt += tilt * 0.3f * (float)sign;
            if (Math.Abs(minTilt) > Mathf.Epsilon && Math.Abs(maxTilt) > Mathf.Epsilon)
            {
                _currentTilt = Mathf.Clamp(_currentTilt, minTilt, maxTilt);
            }
            _objectLocalRotation = Quaternion.Euler(0f, _currentYaw, _currentTilt);
        }
        public void Render(Rect previewRect, Texture2D background = null)
        {
            if (_renderUtility == null) return;

            // Drawn in plain 2D GUI space, BEFORE the 3D render - so it sits behind it as a
            // flat backdrop. Since it's 2D and stretched to the rect rather than placed in the
            // 3D scene, it's completely unaffected by camera zoom (_distance), rotation, or
            // lighting - it just fills the window at a constant size.
            if (background != null)
            {
                GUI.DrawTexture(previewRect, background, ScaleMode.StretchToFill, false);
            }

            _renderUtility.BeginPreview(previewRect, GUIStyle.none);
            HandleInput(previewRect);

            // Position/orient the subject: rotate it about the pivot instead of moving the camera.
            // With the object's own transform originally at (Vector3.zero, identity), rotating "in place"
            // about an arbitrary pivot point means: newPos = pivot + rotation*(originalPos - pivot).
            if (_previewObject != null)
            {
                Vector3 pivot = Pivot;
                Vector3 rotatedPos = pivot + _objectLocalRotation * (Vector3.zero - pivot);
                _previewObject.transform.SetPositionAndRotation(rotatedPos, _objectLocalRotation);
            }

            if (_previewEnvironment != null)
            {
                _previewEnvironment.transform.position = Pivot;
            }

            // Camera stays fixed in orientation - it never orbits - it just looks at the (possibly
            // user-moved) pivot from a constant angle, backing off by _distance for zoom.
            var cam = _renderUtility.camera;
            cam.transform.rotation = _fixedViewAngle;
            cam.transform.position = Pivot - (_fixedViewAngle * Vector3.forward) * _distance;

            _renderUtility.Render();

            // EndPreview() (not EndAndDrawPreview) returns the rendered Texture without drawing
            // it, so we can composite it ourselves with an alpha-blending material instead of
            // PreviewRenderUtility's own blit, which ignores alpha and always draws opaque -
            // that opaque blit is exactly what was painting over the background as solid black.
            Texture rendered = _renderUtility.EndPreview();
            if (background != null)
            {
                if (_alphaBlendMaterial == null)
                {
                    // A dedicated, minimal blit shader with EXPLICIT blend state, rather than
                    // reusing "UI/Default" - that shader is written to run inside Unity's UI
                    // rendering pipeline, which sets up specific GL/blend state beforehand.
                    // Called bare via Graphics.DrawTexture like this, it was inheriting
                    // whatever blend state the just-finished 3D preview render left behind,
                    // which was compositing additively instead of cleanly alpha-blending -
                    // that's what was making the lit result look amplified/brighter over the
                    // background image instead of a normal "object over backdrop" composite.
                    var shader = Shader.Find("Hidden/SpecularSMapPreview/AlphaBlit");
                    if (shader == null)
                    {
                        Debug.LogWarning(
                            "SpecularSmapPreviewControl: 'Hidden/SpecularSMapPreview/AlphaBlit' " +
                            "shader not found - falling back to opaque draw (background image " +
                            "will be covered). Make sure AlphaBlit.shader is in the project.");
                    }
                    else
                    {
                        _alphaBlendMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    }
                }

                if (_alphaBlendMaterial != null)
                    Graphics.DrawTexture(previewRect, rendered, _alphaBlendMaterial);
                else
                    GUI.DrawTexture(previewRect, rendered, ScaleMode.StretchToFill, false);
            }
            else
            {
                // No background assigned - behave exactly as before (opaque draw is fine,
                // there's nothing beneath it that needs to show through).
                GUI.DrawTexture(previewRect, rendered, ScaleMode.StretchToFill, false);
            }

            GUI.Label(new Rect(previewRect.x + 4, previewRect.yMax - 18, 320, 18),
                "LMB: rotate object  Scroll: zoom", EditorStyles.miniLabel);
        }

        private void HandleInput(Rect previewRect)
        {
            Event e = Event.current;
            if (!previewRect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.ScrollWheel)
            {
                _distance = Mathf.Max(0.05f, _distance + e.delta.y * 0.05f * Mathf.Max(_distance, 0.1f));
                e.Use();
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (e.button == 0)
                {
                    // angle drives yaw (horizontal drag), tilt drives pitch (vertical drag),
                    // both scaled/clamped inside Rotate() itself.
                    Rotate(e.delta.x, e.delta.y, MinTilt, MaxTilt);
                    e.Use();
                }
                /*else if (e.button == 1)
                {
                    // Pan the pivot itself (in addition to the explicit Vector3 field) using the
                    // camera's fixed right/up, since the camera direction no longer changes underfoot.
                    Vector3 right = _fixedViewAngle * Vector3.right;
                    Vector3 up = _fixedViewAngle * Vector3.up;
                    float panScale = _distance * 0.002f;
                    PivotOffset -= right * e.delta.x * panScale;
                    PivotOffset += up * e.delta.y * panScale;
                    e.Use();
                }*/
            }
        }
    }
}