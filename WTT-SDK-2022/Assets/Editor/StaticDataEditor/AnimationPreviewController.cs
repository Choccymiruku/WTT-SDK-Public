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

        if (_previewObject != null)
        {
            UnityEngine.Object.DestroyImmediate(_previewObject);
        }

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

        _playable = AnimationClipPlayable.Create(_graph, clip);
        var animator = _previewObject.GetComponent<Animator>();
        var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
        output.SetSourcePlayable(_playable);
        _graph.Play();

        IsPlaying = true;
        AnimationTime = 0f;
        _lastUpdateRealtime = Time.realtimeSinceStartup;
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
            return;
        }

        _playable.SetTime(AnimationTime);
        _graph.Evaluate();
    }

    public void Render(Rect previewRect)
    {
        _renderUtility.BeginPreview(previewRect, GUIStyle.none);
        HandleCameraInput(previewRect);

        var cam = _renderUtility.camera;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 1000f;

        _previewLight.transform.position = cam.transform.position;
        _previewLight.transform.LookAt(_pivot);

        Quaternion rotation = Quaternion.Euler(_orbit.y, _orbit.x, 0f);
        Vector3 position = _pivot - rotation * Vector3.forward * _distance;
        cam.transform.SetPositionAndRotation(position, rotation);

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