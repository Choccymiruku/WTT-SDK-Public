using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public class AudioNotification
{
    private static readonly MethodInfo playPreviewClipMethod;
    private static readonly MethodInfo stopAllPreviewClipsMethod;

    private static AudioClip playConfirm;
    private static AudioClip finalizeEvent;
    private static AudioClip removeEvent;
    private static AudioClip refreshPrefab;

    static AudioNotification()
    {
        Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

        if (audioUtilType != null)
        {
            playPreviewClipMethod = audioUtilType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "PlayPreviewClip");

            stopAllPreviewClipsMethod = audioUtilType.GetMethod(
                "StopAllPreviewClips",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        LoadAudioClips();
    }

    private static void LoadAudioClips()
    {
        playConfirm = LoadClip("Confirm");
        finalizeEvent = LoadClip("Finalize");
        removeEvent = LoadClip("Remove");
        refreshPrefab = LoadClip("Refresh");
    }

    private static AudioClip LoadClip(string name)
    {
        string[] guids = AssetDatabase.FindAssets($"{name} t:AudioClip");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Prefer Assets/Editor/Sounds/
            if (path.Replace("\\", "/").Contains("/Editor/Sounds/"))
            {
                return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }

        // Fallback: any AudioClip with that name
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        Debug.LogWarning($"AudioNotification: Could not find AudioClip '{name}'.");
        return null;
    }

    public static void PlayClick() => Play(playConfirm);
    public static void PlayFinalize() => Play(finalizeEvent);
    public static void PlayRemove() => Play(removeEvent);
    public static void PlayRefresh() => Play(refreshPrefab);

    public static void Play(AudioClip clip, bool loop = false)
    {
        if (clip == null || playPreviewClipMethod == null)
            return;

        stopAllPreviewClipsMethod?.Invoke(null, null);

        ParameterInfo[] parameters = playPreviewClipMethod.GetParameters();
        object[] args = new object[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            Type t = parameters[i].ParameterType;

            if (t == typeof(AudioClip))
                args[i] = clip;
            else if (t == typeof(int))
                args[i] = 0;
            else if (t == typeof(bool))
                args[i] = loop;
            else if (t == typeof(float))
                args[i] = 0f;
            else
                args[i] = Type.Missing;
        }

        playPreviewClipMethod.Invoke(null, args);
    }

    public static void Stop()
    {
        stopAllPreviewClipsMethod?.Invoke(null, null);
    }

    [InitializeOnLoadMethod]
    private static void ReloadOnProjectChange()
    {
        EditorApplication.projectChanged += LoadAudioClips;
    }
}