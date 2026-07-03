using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Plays an AudioClip inside the Editor, outside of Play Mode.
///
/// This is why Sound events previously produced no audio: standard runtime playback
/// (AudioSource / AudioSource.PlayClipAtPoint) spawns real objects, but Unity's audio
/// DSP graph is only actively ticking during Play Mode - it does nothing audible from
/// an EditorWindow at edit time. The Project window's own "click an audio clip to hear
/// it" preview goes through a separate, internal, editor-only pipeline
/// (UnityEditor.AudioUtil) instead. That's what this wraps via reflection, since
/// AudioUtil isn't part of the public API.
/// </summary>
internal static class EditorAudioPreview
{
    private static readonly MethodInfo PlayMethod = FindPlayMethod();

    public static void Play(AudioClip clip)
    {
        if (clip == null)
        {
            return;
        }

        if (PlayMethod == null)
        {
            Debug.LogWarning("[StaticDataEditor] Could not find an editor audio preview method on UnityEditor.AudioUtil " +
                              "for this Unity version. Sound events cannot be previewed.");
            return;
        }

        ParameterInfo[] parameters = PlayMethod.GetParameters();
        object[] args;

        if (parameters.Length == 1)
        {
            args = new object[] { clip };
        }
        else if (parameters.Length == 3)
        {
            args = new object[] { clip, 0, false };
        }
        else
        {
            Debug.LogWarning("[StaticDataEditor] Unexpected AudioUtil play-clip method signature; cannot preview sound.");
            return;
        }

        PlayMethod.Invoke(null, args);
    }

    private static MethodInfo FindPlayMethod()
    {
        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        if (audioUtilType == null)
        {
            return null;
        }

        // The method name/signature has changed across Unity versions - try known variants.
        MethodInfo method = audioUtilType.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);

        if (method != null)
        {
            return method;
        }

        method = audioUtilType.GetMethod(
            "PlayClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(AudioClip) },
            null);

        return method;
    }
}
