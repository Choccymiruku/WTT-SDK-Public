using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Watches the timeline as it plays and fires an editor-preview playback call
/// whenever the playhead crosses a "Sound" event whose String Param matches an entry
/// in the container's sound library.
///
/// Matching rule: names are compared after stripping the "Snd" prefix from whichever
/// side has it (either side, or neither, or both) - e.g. String Param "BoxHit" matches
/// container EventName "SndBoxHit", and String Param "SndBoxHit" also matches
/// EventName "SndBoxHit" directly. What matters is the name underneath the prefix.
/// </summary>
internal sealed class SoundEventPlayer
{
    private const string SoundNamePrefix = "Snd";
    private const string SoundFunctionName = "Sound";

    /// <summary>
    /// Call once per playing frame with the normalized time range covered since the
    /// last call. Handles the wrap-around case where playback looped past 1.0 back to 0.0.
    /// </summary>
    public void CheckAndPlay(float previousNormalizedTime, float currentNormalizedTime,
        IReadOnlyList<StagedAnimationEvent> events, IReadOnlyList<SoundEventEntry> soundLibrary)
    {
        if (events == null || soundLibrary == null || soundLibrary.Count == 0)
        {
            return;
        }

        foreach (StagedAnimationEvent evt in events)
        {
            if (evt.FunctionName != SoundFunctionName)
            {
                continue;
            }

            if (DidCrossTime(previousNormalizedTime, currentNormalizedTime, evt.NormalizedTime))
            {
                TryPlay(evt.StringParam, soundLibrary);
            }
        }
    }

    private static bool DidCrossTime(float previous, float current, float eventTime)
    {
        if (current >= previous)
        {
            return eventTime > previous && eventTime <= current;
        }

        // Playback wrapped around (looped) between the previous and current frame.
        return eventTime > previous || eventTime <= current;
    }

    private static void TryPlay(string stringParam, IReadOnlyList<SoundEventEntry> soundLibrary)
    {
        if (string.IsNullOrEmpty(stringParam))
        {
            return;
        }

        foreach (SoundEventEntry entry in soundLibrary)
        {
            if (NamesMatch(entry.EventName, stringParam))
            {
                PlayRandomClip(entry.SoundClips);
                return;
            }
        }
    }

    private static bool NamesMatch(string eventName, string stringParam)
    {
        if (string.IsNullOrEmpty(eventName) || string.IsNullOrEmpty(stringParam))
        {
            return false;
        }

        if (string.Equals(eventName, stringParam, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(StripPrefix(eventName), StripPrefix(stringParam), StringComparison.Ordinal);
    }

    private static string StripPrefix(string name)
    {
        return name.StartsWith(SoundNamePrefix, StringComparison.Ordinal) ? name.Substring(SoundNamePrefix.Length) : name;
    }

    private static void PlayRandomClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
        {
            return;
        }

        AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        // Editor-only preview playback - see EditorAudioPreview for why this is needed
        // instead of AudioSource.PlayClipAtPoint (which is silent outside Play Mode).
        EditorAudioPreview.Play(clip);
    }
}