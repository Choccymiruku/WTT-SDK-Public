using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Owns everything related to previewing an <see cref="AnimationClip"/> in a small
/// 3D viewport: the PlayableGraph, the preview render utility, camera orbit/pan/zoom,
/// and playback time. Implements <see cref="IDisposable"/> so cleanup is symmetric
/// with Initialize.
/// </summary>
internal sealed class AnimationPreviewController : IDisposable
{
    private PlayableGraph _graph;
    private PreviewRenderUtility _renderUtility;
    private GameObject _previewObject;
    private Light _previewLight;
    private AnimationClipPlayable _playable;

    private Vector2 _orbit = new Vector2(120f, -20f);
    private float _distance = 5f;
    private Vector3 _pivot = Vector3.zero;
    private float _lastUpdateRealtime;
    private AnimationClip _activeClip;
    private GameObject _activePreviewPrefab;

    public bool IsPlaying { get; private set; }
    public float AnimationTime { get; private set; }

    public void Initialize()
    {
        _graph = PlayableGraph.Create();
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _renderUtility = new PreviewRenderUtility { cameraFieldOfView = 30f };

        _previewLight = new GameObject("Preview Light").AddComponent<Light>();
        _previewLight.type = LightType.Directional;
        _previewLight.intensity = 1.0f;
        _renderUtility.AddSingleGO(_previewLight.gameObject);
    }

    public void Dispose()
    {
        if (_graph.IsValid())
        {
            _graph.Destroy();
        }

        _renderUtility?.Cleanup();

        if (_previewLight != null)
        {
            UnityEngine.Object.DestroyImmediate(_previewLight.gameObject);
        }

        if (_previewObject != null)
        {
            UnityEngine.Object.DestroyImmediate(_previewObject);
        }
    }

    public void Play(AnimationClip clip, GameObject userPreviewPrefab)
    {
        if (clip == null)
        {
            return;
        }

        // Resuming (same clip + same preview prefab as last time, and the playable is
        // still set up) picks up from the current AnimationTime instead of restarting -
        // that covers both "unpaused after Stop()" and "pressed Play after scrubbing
        // while stopped". Anything else (a different clip, a different preview prefab,
        // or the very first Play() this session) is treated as a fresh start.
        bool canResume = _playable.IsValid() && _previewObject != null
                          && _activeClip == clip && _activePreviewPrefab == userPreviewPrefab;

        if (!canResume)
        {
            if (_previewObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewObject);
                _previewObject = null;
            }
            CreatePreviewObject(userPreviewPrefab);

            _playable = AnimationClipPlayable.Create(_graph, clip);
            var animator = _previewObject.GetComponent<Animator>();
            var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
            output.SetSourcePlayable(_playable);

            _activeClip = clip;
            _activePreviewPrefab = userPreviewPrefab;
            AnimationTime = 0f;
        }

        // Make sure the graph reflects AnimationTime immediately - matters both for a
        // fresh start (time just reset to 0) and for resuming after the user scrubbed
        // to a new position while stopped.
        _playable.SetTime(AnimationTime);
        _graph.Play();

        IsPlaying = true;
        _lastUpdateRealtime = Time.realtimeSinceStartup;
    }

    /// <summary>
    /// Makes sure a preview object exists without requiring Play() to have been called
    /// yet - used so First Person mode can locate a Camera on the preview prefab even
    /// before the user has pressed Play.
    /// </summary>
    public void EnsurePreviewObject(GameObject userPreviewPrefab)
    {
        if (_previewObject != null)
        {
            return;
        }

        CreatePreviewObject(userPreviewPrefab);
    }

    /// <summary>Finds a Camera on (or under) the currently instantiated preview object, if any.</summary>
    public bool TryGetPreviewObjectCamera(out Camera camera)
    {
        camera = _previewObject != null ? _previewObject.GetComponentInChildren<Camera>(true) : null;
        return camera != null;
    }

    private void CreatePreviewObject(GameObject userPreviewPrefab)
    {
        if (userPreviewPrefab != null)
        {
            _previewObject = UnityEngine.Object.Instantiate(userPreviewPrefab);
            _renderUtility.AddSingleGO(_previewObject);

            if (_previewObject.GetComponent<Animator>() == null)
            {
                _previewObject.AddComponent<Animator>();
            }
        }
        else
        {
            _previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _renderUtility.AddSingleGO(_previewObject);
        }
    }

    public void Stop()
    {
        _graph.Stop();
        IsPlaying = false;
    }

    /// <summary>Advances playback by real elapsed time. Call once per GUI frame while playing.</summary>
    public void Tick(AnimationClip clip)
    {
        if (!IsPlaying || clip == null)
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        AnimationTime += now - _lastUpdateRealtime;
        _lastUpdateRealtime = now;

        if (AnimationTime > clip.length)
        {
            AnimationTime = 0f;
        }

        _playable.SetTime(AnimationTime);
        _graph.Evaluate();
    }

    /// <summary>Jumps to a specific time, e.g. when the user drags the progress slider or timeline playhead.</summary>
    public void Scrub(float time)
    {
        AnimationTime = time;

        if (!_playable.IsValid())
        {
            // Play() hasn't been called yet this session, so there's no PlayableGraph
            // node to scrub - AnimationTime is still updated above so the UI (playhead,
            // Progress slider) stays correct. Without this guard, SetTime() below throws
            // on the invalid handle, which aborts the caller before it can call
            // e.Use()/Repaint() - that's what made a drag only "catch up" on mouse-up.
            return;
        }

        _playable.SetTime(AnimationTime);
        _graph.Evaluate();
    }

    /// <summary>
    /// Renders the preview. If <paramref name="firstPersonCamera"/> is provided, the
    /// preview camera matches its position/rotation/field-of-view every frame instead
    /// of the usual orbit/pan/zoom, and mouse-driven camera controls are skipped
    /// entirely (locked) while it's active.
    /// </summary>
    public void Render(Rect previewRect, Camera firstPersonCamera = null)
    {
        _renderUtility.BeginPreview(previewRect, GUIStyle.none);

        if (firstPersonCamera == null)
        {
            HandleCameraInput(previewRect);
        }

        var cam = _renderUtility.camera;

        if (firstPersonCamera != null)
        {
            cam.nearClipPlane = firstPersonCamera.nearClipPlane;
            cam.farClipPlane = firstPersonCamera.farClipPlane;
            cam.fieldOfView = firstPersonCamera.fieldOfView;

            // The camera's own GameObject is rotated 90 degrees on its local X axis
            // before being applied to the preview camera - post-multiplying keeps this
            // relative to the camera's own local axes rather than the world's.
            Quaternion correctedRotation = firstPersonCamera.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
            cam.transform.SetPositionAndRotation(firstPersonCamera.transform.position, correctedRotation);

            _previewLight.transform.position = cam.transform.position;
            _previewLight.transform.rotation = cam.transform.rotation;
        }
        else
        {
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
            cam.fieldOfView = 75;

            _previewLight.transform.position = cam.transform.position;
            _previewLight.transform.LookAt(_pivot);

            Quaternion rotation = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
            Vector3 position = _pivot - rotation * Vector3.forward * _distance;
            cam.transform.SetPositionAndRotation(position, rotation);
        }

        _renderUtility.Render();
        _renderUtility.EndAndDrawPreview(previewRect);
    }

    private void HandleCameraInput(Rect previewRect)
    {
        Event e = Event.current;
        if (!previewRect.Contains(e.mousePosition))
        {
            return;
        }

        if (e.type == EventType.ScrollWheel)
        {
            _distance += e.delta.y * 0.05f;
            e.Use();
        }
        else if (e.type == EventType.MouseDrag)
        {
            if (e.button == 0)
            {
                _orbit.x += e.delta.x * Mathf.Lerp(0.1f, 1f, _distance / 10f);
                _orbit.y += e.delta.y * Mathf.Lerp(0.1f, 1f, _distance / 10f);
                e.Use();
            }
            else if (e.button == 1)
            {
                var camTransform = _renderUtility.camera.transform;
                _pivot += 0.02f * -e.delta.x * Mathf.Lerp(0.1f, 1f, _distance / 75f) * camTransform.right;
                _pivot -= 0.02f * -e.delta.y * Mathf.Lerp(0.1f, 1f, _distance / 75f) * camTransform.up;
                e.Use();
            }
        }
    }
}