using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>One entry read from a WeaponSoundPlayer's "Additional Sounds" list.</summary>
internal readonly struct SoundEventEntry
{
    public readonly string EventName;
    public readonly AudioClip[] SoundClips;

    public SoundEventEntry(string eventName, AudioClip[] clips)
    {
        EventName = eventName;
        SoundClips = clips ?? new AudioClip[0];
    }
}

/// <summary>
/// Reads sound event data off a "Container" prefab. Looks specifically for a component
/// named <c>WeaponSoundPlayer</c> that inherits from <c>BaseSoundPlayer</c>, then reads
/// its "Additional Sounds" list (a list of serializable elements exposing EventName and
/// SoundClips; RollOff and Volume are intentionally ignored).
///
/// Everything here is done by name via reflection rather than a compiled reference,
/// since WeaponSoundPlayer / BaseSoundPlayer / the sound-element type live outside this
/// editor tool's assembly reference. If your actual field name for "Additional Sounds"
/// doesn't contain "additionalsound" once normalized, update <see cref="SoundsListFieldHint"/>.
/// </summary>
internal sealed class SoundContainerAccessor
{
    private const string PlayerTypeName = "WeaponSoundPlayer";
    private const string BasePlayerTypeName = "BaseSoundPlayer";
    private const string SoundsListFieldHint = "additionalsound";
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public bool TryGetSoundEvents(GameObject container, out List<SoundEventEntry> entries, out string error)
    {
        entries = new List<SoundEventEntry>();
        error = null;

        if (container == null)
        {
            error = "No container assigned.";
            return false;
        }

        Component player = FindWeaponSoundPlayerComponent(container, out error);
        if (player == null)
        {
            return false;
        }

        FieldInfo listField = FindSoundsListField(player.GetType());
        if (listField == null)
        {
            error = $"Could not find an 'Additional Sounds' list field on {player.GetType().Name}.";
            return false;
        }

        if (!(listField.GetValue(player) is IEnumerable rawList))
        {
            error = "The Additional Sounds field was null or not a list.";
            return false;
        }

        foreach (object element in rawList)
        {
            if (element == null)
            {
                continue;
            }

            Type elementType = element.GetType();
            string eventName = GetMemberValue<string>(element, elementType, "EventName");
            AudioClip[] clips = GetMemberValue<AudioClip[]>(element, elementType, "SoundClips");

            if (!string.IsNullOrEmpty(eventName))
            {
                entries.Add(new SoundEventEntry(eventName, clips));
            }
        }

        return true;
    }

    private static Component FindWeaponSoundPlayerComponent(GameObject container, out string error)
    {
        error = null;
        bool foundWrongType = false;

        foreach (Component component in container.GetComponents<Component>())
        {
            if (component == null)
            {
                continue;
            }

            Type type = component.GetType();
            if (type.Name != PlayerTypeName)
            {
                continue;
            }

            if (InheritsFromByName(type, BasePlayerTypeName))
            {
                return component;
            }

            foundWrongType = true;
        }

        error = foundWrongType
            ? $"Found a '{PlayerTypeName}' component but it does not inherit from '{BasePlayerTypeName}'."
            : $"No '{PlayerTypeName}' component (inheriting '{BasePlayerTypeName}') was found on '{container.name}'.";
        return null;
    }

    private static bool InheritsFromByName(Type type, string baseTypeName)
    {
        for (Type t = type.BaseType; t != null; t = t.BaseType)
        {
            if (t.Name == baseTypeName)
            {
                return true;
            }
        }
        return false;
    }

    private static FieldInfo FindSoundsListField(Type playerType)
    {
        for (Type t = playerType; t != null; t = t.BaseType)
        {
            foreach (FieldInfo field in t.GetFields(MemberFlags))
            {
                string normalized = field.Name.Replace("_", string.Empty).ToLowerInvariant();
                if (normalized.Contains(SoundsListFieldHint) && typeof(IEnumerable).IsAssignableFrom(field.FieldType))
                {
                    return field;
                }
            }
        }
        return null;
    }

    private static T GetMemberValue<T>(object instance, Type type, string memberName)
    {
        FieldInfo field = type.GetField(memberName, MemberFlags);
        if (field != null && typeof(T).IsAssignableFrom(field.FieldType))
        {
            return (T)field.GetValue(instance);
        }

        PropertyInfo prop = type.GetProperty(memberName, MemberFlags);
        if (prop != null && typeof(T).IsAssignableFrom(prop.PropertyType))
        {
            return (T)prop.GetValue(instance);
        }

        return default;
    }
}
