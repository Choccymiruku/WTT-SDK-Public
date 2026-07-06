using System;
using System.Collections.Generic;
using AnimationEventSystem;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window for authoring animation events + conditions on an
/// <see cref="AnimatorControllerStaticData"/> asset, with a live preview and a
/// draggable timeline event (in the spirit of Unreal's AnimMontage editor) instead of raw
/// index fields.
///
/// Workflow:
///  1. Assign the Static Data asset and pick an Events Collection Index to work on.
///     Can additionally assign container prefab to directly use sound from AdditionalSound
///  3. Configure a function/parameters/conditions in the inspector fields, then
///     "Add To Timeline" to stage it as a box on the track. Drag boxes to reposition
///     them in time; click one to re-select and edit it.
///  4. "Finalize Event Collection" writes everything currently staged into the real
///     asset in one pass. Nothing is written to the asset before that point.
/// </summary>
public class StaticDataEditor : EditorWindow
{
    private readonly AnimatorStaticDataAccessor _accessor = new AnimatorStaticDataAccessor();
    private readonly SoundContainerAccessor _soundAccessor = new SoundContainerAccessor();
    private readonly SoundEventPlayer _soundPlayer = new SoundEventPlayer();
    private AnimationPreviewController _preview;

    // Static data navigation
    private AnimatorControllerStaticData staticData;
    private AnimatorControllerStaticData _lastStaticData;
    private int eventsCollectionIndex;
    private int _lastEventsCollectionIndex = -1;

    // Staging timeline
    private readonly Dictionary<int, StagedEventCollection> _stagedCollections = new Dictionary<int, StagedEventCollection>();
    private int _selectedStagedIndex = -1;
    private int _draggingStagedIndex = -1;

    // Animation event parameter fields (mirrors AnimationEventParameter)
    private bool paramBool;
    private int paramInt;
    private float paramFloat;
    private string paramString = string.Empty;
    private int paramType;
    private int selectedFunctionIndex;
    private bool useContainerSound;
    private int soundEventDropdownIndex;

    // Event condition fields (mirrors EventCondition)
    private int conditionNameEnum;
    private int condType;
    private float condFloat;
    private bool condBool;
    private int condInt;
    private int mode;
    private int eventConditionIndex;
    private bool showEventConditions;

    // Sound container (item 1 & 2)
    private GameObject soundContainer;
    private GameObject _lastSoundContainer;
    private List<SoundEventEntry> _soundLibrary = new List<SoundEventEntry>();
    private string _soundLibraryError;

    // Preview inputs
    private AnimationClip animationClip;
    private GameObject userPreviewObject;
    private GameObject animationPrefab;
    private GameObject _lastAnimationPrefab;
    private AnimationClip[] _prefabAnimationClips = new AnimationClip[0];
    private string _animationPrefabWarning;
    private int _prefabClipDropdownIndex;
    
    //Notification stuff;
    private string helpMessage;
    private MessageType helpType;
    private double hideTime ;
    private bool showNotif;

    [MenuItem("Custom Windows/Static Data Editor")]
    public static void ShowWindow()
    {
        GetWindow<StaticDataEditor>("Static Data Editor");
    }

    private void OnEnable()
    {
        _preview = new AnimationPreviewController();
        _preview.Initialize();

        // Needed so MouseMove events actually reach OnGUI, which is how the timeline
        // boxes know to expand on hover without requiring a click.
        wantsMouseMove = true;
    }

    private void OnDisable()
    {
        _preview?.Dispose();
    }

    private void OnGUI()
    {
        GUILayout.Label("Static Data Editor", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            staticData = (AnimatorControllerStaticData)EditorGUILayout.ObjectField("Static Data", staticData, typeof(AnimatorControllerStaticData), false);
            using (new EditorGUI.DisabledScope(staticData == null))
            {
                if (GUILayout.Button("Fill Event", GUILayout.Width(80)))
                {
                    RefreshEvent();
                    RefillEvent();
                }
            }
        }
        
        if (staticData != _lastStaticData)
        {
            _lastStaticData = staticData;
            _stagedCollections.Clear();
            _selectedStagedIndex = -1;
        }

        eventsCollectionIndex = Mathf.Max(0, EditorGUILayout.IntField("Events Collection Index", eventsCollectionIndex));
        if (eventsCollectionIndex != _lastEventsCollectionIndex)
        {
            _lastEventsCollectionIndex = eventsCollectionIndex;
            _selectedStagedIndex = -1;
        }

        GUILayout.Space(6);
        DrawSoundContainerSection();
        GUILayout.Space(6);

        if (staticData == null)
        {
            EditorGUILayout.HelpBox("Please assign an AnimatorControllerStaticData object.", MessageType.Warning);
        }
        else
        {
            DrawStaticDataEditor();
        }

        GUILayout.Space(10);
        DrawAnimationPreviewSection();
    }

    // ---------------------------------------------------------------
    // Sound container
    // ---------------------------------------------------------------

    private void DrawSoundContainerSection()
    {
        EditorGUILayout.LabelField("Sound Container", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            soundContainer = (GameObject)EditorGUILayout.ObjectField("Container", soundContainer, typeof(GameObject), false);

            using (new EditorGUI.DisabledScope(soundContainer == null))
            {
                if (GUILayout.Button("Refresh Container", GUILayout.Width(130)))
                {
                    RefreshSoundLibrary();
                    RefreshContainer();
                }
            }
        }

        if (soundContainer != _lastSoundContainer)
        {
            _lastSoundContainer = soundContainer;
            RefreshSoundLibrary();
        }

        if (soundContainer == null)
        {
            EditorGUILayout.HelpBox("Assign a prefab with a WeaponSoundPlayer component to enable Sound event playback.", MessageType.Info);
        }
        else if (_soundLibraryError != null)
        {
            EditorGUILayout.HelpBox(_soundLibraryError, MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox($"Loaded {_soundLibrary.Count} sound event(s) from WeaponSoundPlayer.", MessageType.None);
        }
    }

    private void RefreshSoundLibrary()
    {
        _soundLibrary.Clear();
        _soundLibraryError = null;

        if (soundContainer == null)
        {
            return;
        }

        if (!_soundAccessor.TryGetSoundEvents(soundContainer, out List<SoundEventEntry> entries, out string error))
        {
            _soundLibraryError = error;
            return;
        }

        _soundLibrary = entries;
    }

    // ---------------------------------------------------------------
    // Static data / timeline editing
    // ---------------------------------------------------------------

    private void DrawStaticDataEditor()
    {
        List<EventsCollection> eventsCollections = _accessor.GetEventsCollections(staticData);
        if (eventsCollections == null)
        {
            EditorGUILayout.HelpBox("Could not read event collections from this asset (reflection target missing).", MessageType.Error);
            return;
        }

        EnsureListSize(eventsCollections, eventsCollectionIndex + 1, CreateEmptyEventsCollection);
        EventsCollection eventsCollection = eventsCollections[eventsCollectionIndex];
        StagedEventCollection staged = GetOrLoadStaged(eventsCollection);

        EditorGUILayout.HelpBox($"Editing Event Collection Index {eventsCollectionIndex}  \u2022  {staged.Events.Count} event(s) staged", MessageType.None);

        DrawTimeline(staged);
        GUILayout.Space(6);
        DrawEventInspector(staged);

        GUILayout.Space(10);
        using (new EditorGUI.DisabledScope(staged.Events.Count == 0))
        {
            if (GUILayout.Button("Finalize Event Collection", GUILayout.Height(28)))
            {
                staged.WriteTo(eventsCollection, _accessor);
                EditorUtility.SetDirty(staticData);
                AssetDatabase.SaveAssets();
                _accessor.RunOnValidate(staticData);
                FinalizeTimeline();
            }
            if (showNotif)
            {
                EditorGUILayout.HelpBox(helpMessage, helpType);
                if (EditorApplication.timeSinceStartup >= hideTime)
                {
                    showNotif = false;
                }
                else
                {
                    Repaint();
                }
            }
        }
    }

    /// <summary>
    /// Draws the notify-track-style timeline: a horizontal strip with a draggable box
    /// per staged event, a red playhead, and 10% ruler ticks.
    /// </summary>
    private void DrawTimeline(StagedEventCollection staged)
    {
        GUILayout.Label("Timeline", EditorStyles.boldLabel);
        Rect trackRect = EditorGUILayout.GetControlRect(GUILayout.Height(70));
        EditorGUI.DrawRect(trackRect, new Color(0.15f, 0.15f, 0.15f));

        for (int i = 0; i <= 10; i++)
        {
            float x = trackRect.x + trackRect.width * (i / 10f);
            EditorGUI.DrawRect(new Rect(x, trackRect.y, 1, trackRect.height), new Color(1f, 1f, 1f, 0.08f));
        }

        if (animationClip != null && animationClip.length > 0f)
        {
            float playheadX = trackRect.x + trackRect.width * Mathf.Clamp01(_preview.AnimationTime / animationClip.length);
            EditorGUI.DrawRect(new Rect(playheadX - 1, trackRect.y, 2, trackRect.height), Color.red);
        }

        int[] displayIndices = staged.ComputeDisplayIndices();
        const float collapsedSize = 18f;
        const float expandedWidth = 100f;
        const float expandedHeight = 40f;
        Event e = Event.current;

        var centeredNumberStyle = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };

        for (int i = 0; i < staged.Events.Count; i++)
        {
            StagedAnimationEvent evt = staged.Events[i];
            // The LEFT EDGE of the box is the actual trigger point - this is what the
            // playhead is compared against, and what gets written as the event's time.
            // Anchoring on the left edge (rather than centering the box on it) means an
            // event dragged all the way to the start or end of the track lands on an
            // exact 0.0 or 1.0, instead of being off by half the box's width.
            float anchorX = trackRect.x + trackRect.width * evt.NormalizedTime;
            float centerY = trackRect.y + trackRect.height / 2f;

            // Hover/click always target this fixed small zone at the event's anchor point,
            // regardless of whether the box is currently drawn collapsed or expanded -
            // otherwise the hit target would jump around as the box grows/shrinks.
            var hitRect = new Rect(anchorX, centerY - collapsedSize / 2f, collapsedSize, collapsedSize);

            bool isSelected = i == _selectedStagedIndex;
            bool isDragging = i == _draggingStagedIndex;
            bool isHovered = hitRect.Contains(e.mousePosition);
            bool isExpanded = isHovered || isDragging;

            Rect boxRect = isExpanded
                ? new Rect(anchorX, centerY - expandedHeight / 2f, expandedWidth, expandedHeight)
                : hitRect;

            Color boxColor = evt.FunctionName == "Sound" ? new Color(0.25f, 0.55f, 0.85f) : new Color(0.35f, 0.35f, 0.35f);
            if (isSelected)
            {
                boxColor = Color.Lerp(boxColor, Color.white, 0.35f);
            }
            else if (isHovered || isDragging)
            {
                boxColor = Color.Lerp(boxColor, Color.white, 0.15f);
            }

            // A thin marker at the exact anchor point, so the trigger position stays
            // visible even when the box (which extends to the right of it) gets clipped
            // by the edge of the track near time 1.0.
            EditorGUI.DrawRect(new Rect(anchorX - 1, trackRect.y, 2, trackRect.height), new Color(1f, 1f, 1f, 0.4f));

            EditorGUI.DrawRect(boxRect, boxColor);
            GUI.Box(boxRect, GUIContent.none);

            if (isExpanded)
            {
                string label = evt.FunctionName == "Sound" ? evt.StringParam : evt.FunctionName;
                string caption = $"[{displayIndices[i]}] #{evt.CreationOrder}\n{label}";
                GUI.Label(boxRect, caption, EditorStyles.whiteMiniLabel);
            }
            else
            {
                GUI.Label(boxRect, evt.CreationOrder.ToString(), centeredNumberStyle);
            }

            if (e.type == EventType.MouseDown && hitRect.Contains(e.mousePosition))
            {
                _selectedStagedIndex = i;
                _draggingStagedIndex = i;
                LoadFieldsFromStaged(evt);
                e.Use();
                Repaint();
            }
        }

        if (_draggingStagedIndex >= 0 && _draggingStagedIndex < staged.Events.Count)
        {
            if (e.type == EventType.MouseDrag)
            {
                float t = Mathf.Clamp01((e.mousePosition.x - trackRect.x) / trackRect.width);
                staged.Events[_draggingStagedIndex].NormalizedTime = t;
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp)
            {
                _draggingStagedIndex = -1;
                e.Use();
            }
        }

        if (e.type == EventType.MouseMove)
        {
            // Hover state changed somewhere on the window - repaint so boxes near the
            // cursor expand/collapse immediately rather than waiting for the next
            // incidental repaint.
            Repaint();
        }
    }

    private void DrawEventInspector(StagedEventCollection staged)
    {
        bool hasSelection = _selectedStagedIndex >= 0 && _selectedStagedIndex < staged.Events.Count;
        GUILayout.Label(hasSelection ? $"Editing Event #{staged.Events[_selectedStagedIndex].CreationOrder}" : "New Event", EditorStyles.boldLabel);

        selectedFunctionIndex = EditorGUILayout.Popup("Function Name", selectedFunctionIndex, AnimationEventDefinitions.FunctionNames);
        string functionName = AnimationEventDefinitions.FunctionNames[selectedFunctionIndex];
        bool hasParameter = AnimationEventDefinitions.FunctionsWithParameters.Contains(functionName);

        if (hasParameter)
        {
            DrawAnimationEventParameterFields(functionName);
        }

        GUILayout.Label("Conditions", EditorStyles.boldLabel);
        eventConditionIndex = Mathf.Max(0, EditorGUILayout.IntField("Condition Index", eventConditionIndex));
        showEventConditions = EditorGUILayout.Toggle("Show Event Conditions", showEventConditions);
        if (showEventConditions)
        {
            DrawConditionFields();
        }

        GUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add To Timeline"))
            {
                StagedAnimationEvent newEvent = BuildStagedEventFromFields(staged.NextCreationOrder++);
                newEvent.NormalizedTime = animationClip != null && animationClip.length > 0f
                    ? _preview.AnimationTime / animationClip.length
                    : 0f;
                staged.Events.Add(newEvent);
                _selectedStagedIndex = staged.Events.Count - 1;
                AddEvent();
            }

            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                if (GUILayout.Button("Update Selected"))
                {
                    ApplyFieldsToStaged(staged.Events[_selectedStagedIndex]);
                    UpdateEvent();
                }

                if (GUILayout.Button("Remove Selected"))
                {
                    staged.Events.RemoveAt(_selectedStagedIndex);
                    _selectedStagedIndex = -1;
                    RemoveEvent();
                }
            }
        }
    }

    private void DrawAnimationEventParameterFields(string functionName)
    {
        EditorGUILayout.LabelField("Animation Event Parameter", EditorStyles.boldLabel);

        switch (AnimationEventDefinitions.FunctionNames[selectedFunctionIndex])
        {
            case "Sound":
                useContainerSound = EditorGUILayout.Toggle("Use Container Sound", useContainerSound);
                if (useContainerSound)
                {
                    DrawContainerSoundDropdown();
                }
                else
                {
                    paramString = EditorGUILayout.TextField("String Param", paramString);
                }
                paramType = EditorGUILayout.Popup("Param Type", paramType, AnimationEventDefinitions.ParamTypeNames);
                paramType = 3;
                break;
            case "ThirdAction":
                paramInt = EditorGUILayout.IntField("Int Param", paramInt);
                paramType = EditorGUILayout.Popup("Param Type", paramType, AnimationEventDefinitions.ParamTypeNames);
                paramType = 1;
                break;
            case "UseProp":
                paramBool = EditorGUILayout.Toggle("Bool Param", paramBool);
                paramType = EditorGUILayout.Popup("Param Type", paramType, AnimationEventDefinitions.ParamTypeNames);
                paramType = 4;
                break;
            default:
                paramType = EditorGUILayout.Popup("Param Type", paramType, AnimationEventDefinitions.ParamTypeNames);
                paramType = 0;
                break;
        }
    }

    /// <summary>Lets the user pick a sound by EventName instead of typing it, sourced from the Container.</summary>
    private void DrawContainerSoundDropdown()
    {
        if (_soundLibrary.Count == 0)
        {
            EditorGUILayout.HelpBox("No sounds loaded from the Container. Assign a Container with a WeaponSoundPlayer above.", MessageType.Warning);
            return;
        }

        string[] names = GetStrippedSoundNames();
        soundEventDropdownIndex = Mathf.Clamp(soundEventDropdownIndex, 0, names.Length - 1);
        soundEventDropdownIndex = EditorGUILayout.Popup("Sound Event", soundEventDropdownIndex, names);
        paramString = names[soundEventDropdownIndex];
    }

    /// <summary>Sound library EventNames with the "Snd" prefix removed, matching how String Param stores them.</summary>
    private string[] GetStrippedSoundNames()
    {
        var names = new string[_soundLibrary.Count];
        for (int i = 0; i < _soundLibrary.Count; i++)
        {
            string n = _soundLibrary[i].EventName;
            names[i] = n.StartsWith("Snd", StringComparison.Ordinal) ? n.Substring(3) : n;
        }
        return names;
    }

    private void DrawConditionFields()
    {
        condType = EditorGUILayout.Popup("Condition Type", condType, AnimationEventDefinitions.ConditionTypeNames);
        string[] names = AnimationEventDefinitions.ConditionNamesForTypeIndex(condType);
        conditionNameEnum = EditorGUILayout.Popup("Name", conditionNameEnum, names);

        switch (condType)
        {
            case 0: // Int
                condInt = EditorGUILayout.IntField("Int Value", condInt);
                mode = EditorGUILayout.Popup("Condition Mode", mode, AnimationEventDefinitions.ConditionModeNames);
                break;
            case 1: // Float
                condFloat = EditorGUILayout.FloatField("Float Value", condFloat);
                mode = EditorGUILayout.Popup("Condition Mode", mode, AnimationEventDefinitions.ConditionModeNames);
                break;
            case 2: // Boolean
                condBool = EditorGUILayout.Toggle("Bool Value", condBool);
                break;
        }
    }

    private StagedAnimationEvent BuildStagedEventFromFields(int creationOrder)
    {
        var s = new StagedAnimationEvent { CreationOrder = creationOrder };
        ApplyFieldsToStaged(s);
        return s;
    }

    private void ApplyFieldsToStaged(StagedAnimationEvent s)
    {
        string functionName = AnimationEventDefinitions.FunctionNames[selectedFunctionIndex];
        bool hasParameter = AnimationEventDefinitions.FunctionsWithParameters.Contains(functionName);

        s.FunctionName = functionName;
        if (hasParameter)
        {
            s.BoolParam = paramBool;
            s.FloatParam = paramFloat;
            s.IntParam = paramInt;
            s.StringParam = paramString;
            s.ParamTypeIndex = paramType;
        }
        else
        {
            s.BoolParam = false;
            s.FloatParam = 0f;
            s.IntParam = 0;
            s.StringParam = string.Empty;
            s.ParamTypeIndex = 0;
        }

        if (showEventConditions)
        {
            EnsureListSize(s.Conditions, eventConditionIndex + 1, () => new StagedCondition());
            string[] names = AnimationEventDefinitions.ConditionNamesForTypeIndex(condType);
            string parameterName = conditionNameEnum < names.Length ? names[conditionNameEnum] : string.Empty;

            StagedCondition c = s.Conditions[eventConditionIndex];
            c.ConditionTypeIndex = condType;
            c.ParameterName = parameterName;

            switch (condType)
            {
                case 0:
                    c.IntValue = condInt; c.FloatValue = 0f; c.BoolValue = false; c.ModeIndex = mode;
                    break;
                case 1:
                    c.FloatValue = condFloat; c.IntValue = 0; c.BoolValue = false; c.ModeIndex = mode;
                    break;
                case 2:
                    c.BoolValue = condBool; c.FloatValue = 0f; c.IntValue = 0; c.ModeIndex = 0;
                    break;
            }
        }
    }

    private void LoadFieldsFromStaged(StagedAnimationEvent s)
    {
        selectedFunctionIndex = Mathf.Max(0, Array.IndexOf(AnimationEventDefinitions.FunctionNames, s.FunctionName));
        paramBool = s.BoolParam;
        paramFloat = s.FloatParam;
        paramInt = s.IntParam;
        paramString = s.StringParam;
        paramType = s.ParamTypeIndex;

        if (s.FunctionName == "Sound" && _soundLibrary.Count > 0)
        {
            int matchIndex = Array.IndexOf(GetStrippedSoundNames(), s.StringParam);
            soundEventDropdownIndex = matchIndex >= 0 ? matchIndex : 0;
        }

        showEventConditions = s.Conditions.Count > 0;
        if (!showEventConditions)
        {
            return;
        }

        eventConditionIndex = Mathf.Clamp(eventConditionIndex, 0, s.Conditions.Count - 1);
        StagedCondition c = s.Conditions[eventConditionIndex];
        condType = c.ConditionTypeIndex;
        string[] names = AnimationEventDefinitions.ConditionNamesForTypeIndex(condType);
        conditionNameEnum = Mathf.Max(0, Array.IndexOf(names, c.ParameterName));
        condInt = c.IntValue;
        condFloat = c.FloatValue;
        condBool = c.BoolValue;
        mode = c.ModeIndex;
    }

    private StagedEventCollection GetOrLoadStaged(EventsCollection collection)
    {
        if (!_stagedCollections.TryGetValue(eventsCollectionIndex, out StagedEventCollection staged))
        {
            staged = StagedEventCollection.LoadFrom(collection, _accessor, animationClip);
            _stagedCollections[eventsCollectionIndex] = staged;
        }
        return staged;
    }

    private EventsCollection CreateEmptyEventsCollection()
    {
        var collection = new EventsCollection();
        _accessor.SetAnimationEvents(collection, new List<AnimationEventSystem.AnimationEvent>());
        return collection;
    }

    private static void EnsureListSize<T>(List<T> list, int size, Func<T> factory)
    {
        while (list.Count < size)
        {
            list.Add(factory());
        }
    }

    // ---------------------------------------------------------------
    // Animation clip preview + sound triggering
    // ---------------------------------------------------------------

    private void DrawAnimationPreviewSection()
    {
        EditorGUILayout.LabelField("Animation Source", EditorStyles.boldLabel);

        // Only one source can drive the clip at a time: a direct clip, or a prefab's
        // clip picked from a dropdown. Whichever one is already set locks the other.
        bool clipLocked = animationPrefab != null;
        bool prefabLocked = !clipLocked && animationClip != null;

        using (new EditorGUI.DisabledScope(clipLocked))
        {
            AnimationClip newClip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", animationClip, typeof(AnimationClip), false);
            if (!clipLocked)
            {
                animationClip = newClip;
            }
        }

        using (new EditorGUI.DisabledScope(prefabLocked))
        {
            animationPrefab = (GameObject)EditorGUILayout.ObjectField("Animation Prefab", animationPrefab, typeof(GameObject), false);
        }

        if (animationPrefab != _lastAnimationPrefab)
        {
            _lastAnimationPrefab = animationPrefab;
            if (animationPrefab == null)
            {
                // Prefab cleared - drop the clip it supplied so the Clip field unlocks cleanly.
                animationClip = null;
            }
            RefreshPrefabAnimationClips();
        }

        if (animationPrefab != null)
        {
            DrawPrefabAnimationDropdown();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview Object", EditorStyles.boldLabel);
        userPreviewObject = (GameObject)EditorGUILayout.ObjectField("User Preview Object", userPreviewObject, typeof(GameObject), false);

        if (animationClip == null)
        {
            EditorGUILayout.HelpBox("Please assign an Animation Clip or an Animation Prefab.", MessageType.Warning);
            return;
        }

        if (GUILayout.Button(_preview.IsPlaying ? "Stop" : "Play"))
        {
            if (_preview.IsPlaying)
            {
                _preview.Stop();
            }
            else
            {
                _preview.Play(animationClip, userPreviewObject);
            }
        }
        
        // preview scrub position and (via the timeline above) where staged events sit.
        float newProgress = EditorGUILayout.Slider("Progress", _preview.AnimationTime / animationClip.length, 0f, 1f);
        float newAnimationTime = newProgress * animationClip.length;
        if (!_preview.IsPlaying && !Mathf.Approximately(newAnimationTime, _preview.AnimationTime))
        {
            _preview.Scrub(newAnimationTime);
            Repaint();
        }

        Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        _preview.Render(previewRect);

        if (_preview.IsPlaying)
        {
            float beforeNormalized = _preview.AnimationTime / animationClip.length;
            _preview.Tick(animationClip);
            float afterNormalized = _preview.AnimationTime / animationClip.length;

            if (staticData != null && _stagedCollections.TryGetValue(eventsCollectionIndex, out StagedEventCollection staged))
            {
                _soundPlayer.CheckAndPlay(beforeNormalized, afterNormalized, staged.Events, _soundLibrary);
            }

            Repaint();
        }
    }

    /// <summary>
    /// Reads the clips available on the assigned object. Handles two cases differently:
    ///  - A regular Prefab with an Animator+Controller or legacy Animation component:
    ///    AnimationUtility.GetAnimationClips picks these up directly.
    ///  - An imported model (FBX, etc.) dragged in with no AnimatorController assigned:
    ///    its clips exist as sub-assets of the source file itself and AnimationUtility
    ///    won't see them, so those are read straight off the import via
    ///    AssetDatabase.LoadAllAssetsAtPath instead.
    /// </summary>
    private void RefreshPrefabAnimationClips()
    {
        _prefabAnimationClips = new AnimationClip[0];
        _animationPrefabWarning = null;
        _prefabClipDropdownIndex = 0;

        if (animationPrefab == null)
        {
            return;
        }

        AnimationClip[] clips = GetClipsFromImportedModel(animationPrefab);
        if (clips == null || clips.Length == 0)
        {
            clips = AnimationUtility.GetAnimationClips(animationPrefab);
        }

        if (clips == null || clips.Length == 0)
        {
            _animationPrefabWarning = $"'{animationPrefab.name}' does not have any animation.";
            return;
        }

        _prefabAnimationClips = clips;
        animationClip = clips[0];
    }

    private static AnimationClip[] GetClipsFromImportedModel(GameObject go)
    {
        string path = GetSourceAssetPath(go);
        if (string.IsNullOrEmpty(path) || !(AssetImporter.GetAtPath(path) is ModelImporter))
        {
            return null;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
        var clips = new List<AnimationClip>();
        foreach (UnityEngine.Object asset in assets)
        {
            // Unity generates internal "__preview__" clips (e.g. for avatar masking) that
            // aren't meant to be picked from a clip list - skip those.
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            {
                clips.Add(clip);
            }
        }

        return clips.Count > 0 ? clips.ToArray() : null;
    }

    /// <summary>
    /// Resolves the source asset path for a GameObject that might be a project asset
    /// (dragged directly from the Project window) or a scene instance of one (dragged
    /// from the Hierarchy) - imported FBX models are commonly used both ways.
    /// </summary>
    private static string GetSourceAssetPath(GameObject go)
    {
        string path = AssetDatabase.GetAssetPath(go);
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
        return source != null ? AssetDatabase.GetAssetPath(source) : null;
    }

    private void RefreshEvent()
    {
        List<EventsCollection> eventsCollections = _accessor.GetEventsCollections(staticData);
        if (!staticData || eventsCollections == null)
        {
            return;
        }
 
        EnsureListSize(eventsCollections, eventsCollectionIndex + 1, CreateEmptyEventsCollection);
        EventsCollection eventsCollection = eventsCollections[eventsCollectionIndex];
 
        _stagedCollections[eventsCollectionIndex] = StagedEventCollection.LoadFrom(eventsCollection, _accessor, animationClip);
        _selectedStagedIndex = -1;
        _draggingStagedIndex = -1;
    }

    private void DrawPrefabAnimationDropdown()
    {
        if (_animationPrefabWarning != null)
        {
            EditorGUILayout.HelpBox(_animationPrefabWarning, MessageType.Warning);
            return;
        }

        var clipNames = new string[_prefabAnimationClips.Length];
        for (int i = 0; i < _prefabAnimationClips.Length; i++)
        {
            clipNames[i] = _prefabAnimationClips[i].name;
        }

        _prefabClipDropdownIndex = Mathf.Clamp(_prefabClipDropdownIndex, 0, clipNames.Length - 1);
        int newIndex = EditorGUILayout.Popup("Prefab Animation", _prefabClipDropdownIndex, clipNames);
        if (newIndex != _prefabClipDropdownIndex)
        {
            _prefabClipDropdownIndex = newIndex;
        }
        animationClip = _prefabAnimationClips[_prefabClipDropdownIndex];
    }

    private void ShowNotif(string message, MessageType type, double time)
    {
        helpMessage = message;
        helpType = type;
        hideTime  = EditorApplication.timeSinceStartup + time;
        showNotif = true;
        Repaint();
    }

    private void RefreshContainer()
    {
        ShowNotif("Container has been Refreshed!", MessageType.Info, 2.5);
    }

    private void RefillEvent()
    {
        ShowNotif("Event Timeline has been refreshed from the static data!", MessageType.Info, 2.5);
    }

    private void UpdateEvent()
    {
        ShowNotif("Selected Event has been updated!", MessageType.Info, 2.5);
    }

    private void RemoveEvent()
    {
        ShowNotif("Selected Event has been removed!", MessageType.Warning, 2.5);
    }

    private void FinalizeTimeline()
    {
        ShowNotif("Timeline Event has been added to the static data!", MessageType.Info, 2.5);
    }

    private void AddEvent()
    {
        ShowNotif("Added Event to Staged Timeline!", MessageType.Info, 2.5);
    }
}