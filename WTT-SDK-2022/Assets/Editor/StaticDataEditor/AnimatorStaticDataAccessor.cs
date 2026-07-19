using System.Collections.Generic;
using System.Reflection;
using AnimationEventSystem;
using UnityEngine;
using AnimationEvent = AnimationEventSystem.AnimationEvent;

/// <summary>
/// Wraps every private-field reflection call the editor needs to make against
/// <see cref="AnimatorControllerStaticData"/> and its related types. Each field is
/// looked up once (cached as `static readonly`); a missing field logs a single clear
/// warning instead of failing silently deep inside a GUI callback.
/// </summary>
internal sealed class AnimatorStaticDataAccessor
{
    private static readonly FieldInfo StateHashToEventsCollectionField =
        typeof(AnimatorControllerStaticData).GetField("_stateHashToEventsCollection", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo AnimationEventsField =
        typeof(EventsCollection).GetField("_animationEvents", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo FunctionNameField =
        typeof(AnimationEvent).GetField("_functionName", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly FieldInfo EventTimeField =
        typeof(AnimationEvent).GetField("_time", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly MethodInfo OnValidateMethod =
        typeof(AnimatorControllerStaticData).GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance);

    static AnimatorStaticDataAccessor()
    {
        WarnIfMissing(StateHashToEventsCollectionField, nameof(StateHashToEventsCollectionField));
        WarnIfMissing(AnimationEventsField, nameof(AnimationEventsField));
        WarnIfMissing(FunctionNameField, nameof(FunctionNameField));
        WarnIfMissing(EventTimeField, nameof(EventTimeField));
    }

    private static void WarnIfMissing(MemberInfo member, string label)
    {
        if (member == null)
        {
            Debug.LogWarning($"[StaticDataEditor] Expected reflection target '{label}' was not found. " +
                              "The underlying data classes may have changed - the editor window will not work correctly until this is updated.");
        }
    }

    public List<EventsCollection> GetEventsCollections(AnimatorControllerStaticData data)
        => StateHashToEventsCollectionField?.GetValue(data) as List<EventsCollection>;

    public List<AnimationEvent> GetAnimationEvents(EventsCollection collection)
        => AnimationEventsField?.GetValue(collection) as List<AnimationEvent>;

    public void SetAnimationEvents(EventsCollection collection, List<AnimationEvent> events)
        => AnimationEventsField?.SetValue(collection, events);

    public void SetFunctionName(AnimationEvent evt, string functionName)
        => FunctionNameField?.SetValue(evt, functionName);

    public string GetFunctionName(AnimationEvent evt)
        => FunctionNameField?.GetValue(evt) as string;

    public void SetEventTime(AnimationEvent evt, float normalizedTime)
        => EventTimeField?.SetValue(evt, normalizedTime);

    public float GetEventTime(AnimationEvent evt)
        => EventTimeField != null ? (float)EventTimeField.GetValue(evt) : 0f;

    public void RunOnValidate(AnimatorControllerStaticData data)
        => OnValidateMethod?.Invoke(data, null);
}
