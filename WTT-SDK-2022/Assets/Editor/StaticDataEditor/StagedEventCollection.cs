using System.Collections.Generic;
using System.Linq;
using AnimationEventSystem;
using UnityEngine;
using AnimationEvent = AnimationEventSystem.AnimationEvent;

/// <summary>A single condition attached to a staged event, mirroring EventCondition's fields.</summary>
internal sealed class StagedCondition
{
    /// <summary>0 = Int, 1 = Float, 2 = Boolean - matches AnimationEventDefinitions.ConditionTypeNames.</summary>
    public int ConditionTypeIndex;
    public string ParameterName = string.Empty;
    public int ModeIndex;
    public int IntValue;
    public float FloatValue;
    public bool BoolValue;
}

/// <summary>
/// A single event as arranged on the timeline. Everything here lives only in the editor
/// until <see cref="StagedEventCollection.WriteTo"/> pushes it into the real asset.
/// </summary>
internal sealed class StagedAnimationEvent
{
    /// <summary>Fixed at creation time; the "5th event created" badge shown on its timeline box (0-based).</summary>
    public int CreationOrder;

    public string FunctionName = AnimationEventDefinitions.FunctionNames[0];

    /// <summary>0-1 position on the timeline (the same convention AnimatorControllerStaticData stores).</summary>
    public float NormalizedTime;

    public bool BoolParam;
    public float FloatParam;
    public int IntParam;
    public string StringParam = string.Empty;
    public int ParamTypeIndex;

    public readonly List<StagedCondition> Conditions = new List<StagedCondition>();
}

/// <summary>
/// In-memory "working copy" of one EventsCollection's events. Events are added, dragged
/// along the timeline, and removed freely here; none of it touches the real
/// AnimatorControllerStaticData asset until <see cref="WriteTo"/> runs (triggered by the
/// "Finalize Event Collection" button) - similar to how an Unreal AnimMontage lets you
/// rearrange notifies on a track before they're considered final.
/// </summary>
internal sealed class StagedEventCollection
{
    public readonly List<StagedAnimationEvent> Events = new List<StagedAnimationEvent>();
    public int NextCreationOrder;

    public static StagedEventCollection LoadFrom(EventsCollection collection, AnimatorStaticDataAccessor accessor, AnimationClip clip)
    {
        var staged = new StagedEventCollection();
        if (collection == null)
        {
            return staged;
        }

        List<AnimationEvent> events = accessor.GetAnimationEvents(collection);
        if (events == null)
        {
            return staged;
        }

        foreach (AnimationEvent evt in events)
        {
            var s = new StagedAnimationEvent
            {
                CreationOrder = staged.NextCreationOrder++,
                FunctionName = accessor.GetFunctionName(evt) ?? AnimationEventDefinitions.FunctionNames[0],
                NormalizedTime = accessor.GetEventTime(evt)
            };

            if (evt.Parameter != null)
            {
                s.BoolParam = evt.Parameter.BoolParam;
                s.FloatParam = evt.Parameter.FloatParam;
                s.IntParam = evt.Parameter.IntParam;
                s.StringParam = evt.Parameter.StringParam ?? string.Empty;
                s.ParamTypeIndex = (int)evt.Parameter.ParamType;
            }

            if (evt.EventConditions != null)
            {
                foreach (EventCondition c in evt.EventConditions)
                {
                    s.Conditions.Add(new StagedCondition
                    {
                        ConditionTypeIndex = (int)c.ConditionParamType,
                        ParameterName = c.ParameterName,
                        ModeIndex = (int)c.ConditionMode,
                        IntValue = c.IntValue,
                        FloatValue = c.FloatValue,
                        BoolValue = c.BoolValue
                    });
                }
            }

            staged.Events.Add(s);
        }

        return staged;
    }

    /// <summary>Writes the staged events (ordered by timeline position) into the real asset.</summary>
    public void WriteTo(EventsCollection collection, AnimatorStaticDataAccessor accessor)
    {
        var events = new List<AnimationEvent>();

        foreach (StagedAnimationEvent s in Events.OrderBy(e => e.NormalizedTime))
        {
            var evt = new AnimationEvent();
            accessor.SetFunctionName(evt, s.FunctionName);
            accessor.SetEventTime(evt, s.NormalizedTime);

            evt.Parameter = new AnimationEventParameter
            {
                BoolParam = s.BoolParam,
                FloatParam = s.FloatParam,
                IntParam = s.IntParam,
                StringParam = s.StringParam,
                ParamType = (EAnimationEventParamType)s.ParamTypeIndex
            };

            if (s.Conditions.Count > 0)
            {
                evt.EventConditions = new List<EventCondition>();
                foreach (StagedCondition c in s.Conditions)
                {
                    evt.EventConditions.Add(new EventCondition
                    {
                        ConditionParamType = (EEventConditionParamTypes)c.ConditionTypeIndex,
                        ParameterName = c.ParameterName,
                        ConditionMode = (EEventConditionModes)c.ModeIndex,
                        IntValue = c.IntValue,
                        FloatValue = c.FloatValue,
                        BoolValue = c.BoolValue
                    });
                }
            }

            events.Add(evt);
        }

        accessor.SetAnimationEvents(collection, events);
    }

    /// <summary>The 0-based index each event would end up at if written out right now (sorted by time).</summary>
    public int[] ComputeDisplayIndices()
    {
        var indices = new int[Events.Count];
        List<int> order = Enumerable.Range(0, Events.Count)
            .OrderBy(i => Events[i].NormalizedTime)
            .ToList();

        for (int displayIndex = 0; displayIndex < order.Count; displayIndex++)
        {
            indices[order[displayIndex]] = displayIndex;
        }

        return indices;
    }
}
