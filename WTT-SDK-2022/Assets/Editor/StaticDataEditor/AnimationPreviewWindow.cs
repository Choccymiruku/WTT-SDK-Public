using UnityEditor;
using UnityEngine;

/// <summary>
/// A separate window that hosts the "Animation Source" through preview-render section
/// of <see cref="StaticDataEditor"/>, so it can be dragged out to its own tab/monitor
/// instead of always living inside the main window.
///
/// This doesn't duplicate any state - it just calls back into the owning
/// StaticDataEditor's drawing method each frame, so both windows are always looking at
/// the exact same AnimationPreviewController, staged data, etc. There is never more
/// than one of these open per StaticDataEditor at a time.
/// </summary>
internal sealed class AnimationPreviewWindow : EditorWindow
{
    private StaticDataEditor _owner;

    public static AnimationPreviewWindow Open(StaticDataEditor owner)
    {
        var window = CreateInstance<AnimationPreviewWindow>();
        window.titleContent = new GUIContent("Animation Preview");
        window._owner = owner;
        window.Show();
        return window;
    }

    private void OnGUI()
    {
        if (_owner == null)
        {
            EditorGUILayout.HelpBox("The owning Static Data Editor window was closed.", MessageType.Warning);
            return;
        }

        _owner.DrawAnimationPreviewSection();
    }

    private void Update()
    {
        // Keep this window repainting on its own even when it doesn't have focus, so
        // playback (Tick-driven) and the live playhead stay smooth while detached.
        Repaint();
    }

    private void OnDestroy()
    {
        _owner?.NotifyPreviewWindowClosed();
    }
}
