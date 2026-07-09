using System;
using System.Collections.Generic;
using AnimationEventSystem;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Editor window for authoring animation events + conditions on an
/// <see cref="AnimatorControllerStaticData"/> asset, with a live clip preview and a
/// draggable timeline (in the spirit of Unreal's AnimMontage editor) instead of raw
/// index fields.
///
/// Workflow:
///  1. Assign the Static Data asset and pick an Events Collection Index to work on.
///  2. Optionally assign a sound Container prefab (a WeaponSoundPlayer) so "Sound"
///     events can actually be heard during preview playback.
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
    private float _timelineZoom = 1f;
    private float _timelineHeight = 90f;
    private Vector2 _timelineScrollPos;
    private bool _resizingTimelineHeight;
    private bool _isPanningTimeline;
    private bool hideSoundEvents;
    private int hideEventAmount;

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
            staticData = (AnimatorControllerStaticData)EditorGUILayout.ObjectField(new GUIContent(
                "Static Data",
                "The static data that is being used" +
                " by the animator controller for Animation Events."), 
                staticData, typeof(AnimatorControllerStaticData), false);
            using (new EditorGUI.DisabledScope(staticData == null))
            {
                if (GUILayout.Button(new GUIContent(
                        "Fill Event",
                        "Grab the event from the current selected index" +
                        " of the static data"), GUILayout.Width(80)))
                {
                    RefreshEvent();
                    RefillEvent();
                    AudioNotification.PlayClick();
                }
            }
        }
        
        if (staticData != _lastStaticData)
        {
            _lastStaticData = staticData;
            _stagedCollections.Clear();
            _selectedStagedIndex = -1;
        }

        eventsCollectionIndex = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent(
            "Events Collection Index",
            "Select the index you want to modify." +
            " MUST BE IN INTEGER. This is an array, the number" +
            " you put in is the element that is being access/modified." +
            " The total Event collection will be " +
            " always +1 of your highest element number."), eventsCollectionIndex));
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
            soundContainer = (GameObject)EditorGUILayout.ObjectField(new GUIContent(
                "Container",
                "Insert the weapon container prefab" +
                " that stores the WeaponSoundPlayer"), soundContainer, typeof(GameObject), false);

            using (new EditorGUI.DisabledScope(soundContainer == null))
            {
                if (GUILayout.Button(new GUIContent(
                        "Refresh",
                        "Refresh both the sound library" +
                        " and animation prefab that is inserted"), GUILayout.Width(80)))
                {
                    RefreshSoundLibrary();
                    RefreshPrefabAnimationClips();
                    RefreshContainer();
                    AudioNotification.PlayRefresh();
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
            if (GUILayout.Button(new GUIContent(
                    "Finalize Event Timeline",
                    "Copy All the staged Event within the timeline" +
                    " and save them into the current Event Collection index" +
                    " of the static data."), GUILayout.Height(28)))
            {
                staged.WriteTo(eventsCollection, _accessor);
                EditorUtility.SetDirty(staticData);
                AssetDatabase.SaveAssets();
                _accessor.RunOnValidate(staticData);
                FinalizeTimeline();
                AudioNotification.PlayFinalize();
            }
        }
    }

    /// <summary>
    /// Draws the notify-track-style timeline: a horizontal, zoomable/scrollable strip
    /// with a draggable (both axes) box per staged event, a red playhead, ruler ticks
    /// labeled with seconds and frame number, and a user-resizable height.
    /// </summary>
    private void DrawTimeline(StagedEventCollection staged)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(new GUIContent(
                "Timeline",
                "Timeline for displaying the Animation Event that exist within the current static data index." +
                " Mouse Scroll wheel to zoom. Middle Mouse to drag (Think of it as blender middle mouse)." +
                " Dragable Event object for retiming. Hold Left Mouse on the red Playhead to move the Position" +
                " where the event will be placed."), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Zoom", GUILayout.Width(36));
            _timelineZoom = GUILayout.HorizontalSlider(_timelineZoom, 1f, 10f, GUILayout.Width(120));
        }
        hideSoundEvents = EditorGUILayout.Toggle(new GUIContent(
            "Hide Sound Event",
            "Hide the Event that is only playing sound"), hideSoundEvents, GUILayout.Width(60));
        hideEventAmount = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent(
            "Hide Event Amount",
            "How many events there are to be hidden. " +
            "Based on creation order, 10 means event numbered " +
            "0-9 will be hidden"), hideEventAmount, GUILayout.Width(220)));

        // The visible (viewport) area is fixed to the user-chosen height; the content
        // inside is wider when zoomed in (scrolls horizontally) and taller than the
        // viewport by design (scrolls vertically), so there's always somewhere for a
        // middle-mouse-button pan to actually go, and lanes have more room to spread out.
        Rect viewportRect = EditorGUILayout.GetControlRect(GUILayout.Height(_timelineHeight));
        EditorGUIUtility.AddCursorRect(viewportRect, MouseCursor.Pan);
        HandleTimelineZoomAndPan(viewportRect);

        float contentWidth = viewportRect.width * _timelineZoom;
        float contentHeight = _timelineHeight * 1.5f;
        var contentRect = new Rect(0, 0, contentWidth, contentHeight);

        _timelineScrollPos = GUI.BeginScrollView(viewportRect, _timelineScrollPos, contentRect, true, true);
        Rect trackRect = contentRect;
        EditorGUI.DrawRect(trackRect, new Color(0.15f, 0.15f, 0.15f));

        DrawTimelineRuler(trackRect);

        if (animationClip != null && animationClip.length > 0f)
        {
            float playheadX = trackRect.x + trackRect.width * Mathf.Clamp01(_preview.AnimationTime / animationClip.length);
            var playheadHitRect = new Rect(playheadX - 4, trackRect.y, 8, trackRect.height);
            HandlePlayheadDrag(playheadHitRect, trackRect);

            playheadX = trackRect.x + trackRect.width * Mathf.Clamp01(_preview.AnimationTime / animationClip.length);
            EditorGUI.DrawRect(new Rect(playheadX - 1, trackRect.y, 2, trackRect.height), Color.red);
        }

        DrawTimelineBoxes(staged, trackRect);

        GUI.EndScrollView();

        DrawTimelineResizeHandle(viewportRect);
    }

    /// <summary>
    /// Lets the red playhead line itself be dragged to scrub, same as the Progress
    /// slider - both read/write the same <see cref="AnimationPreviewController.AnimationTime"/>,
    /// so moving one immediately updates the other on the next repaint.
    /// </summary>
    private void HandlePlayheadDrag(Rect playheadHitRect, Rect trackRect)
    {
        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;

        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (!_preview.IsPlaying && playheadHitRect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId)
                {
                    float t = Mathf.Clamp01((e.mousePosition.x - trackRect.x) / trackRect.width);
                    _preview.Scrub(t * animationClip.length);
                    e.Use();
                    Repaint();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
                break;
        }
    }

    /// <summary>
    /// Scroll wheel while hovering the timeline zooms in/out (keeping the point under the
    /// cursor stationary, the same way most timeline/DCC tools behave); holding down the
    /// middle mouse button and dragging pans the view, Blender-viewport-style.
    /// </summary>
    private void HandleTimelineZoomAndPan(Rect viewportRect)
    {
        Event e = Event.current;
        bool hovering = viewportRect.Contains(e.mousePosition);

        if (hovering && e.type == EventType.ScrollWheel)
        {
            ApplyTimelineZoom(viewportRect, e);
            return;
        }

        if (hovering && e.type == EventType.MouseDown && e.button == 2)
        {
            _isPanningTimeline = true;
            e.Use();
            return;
        }

        if (_isPanningTimeline && e.type == EventType.MouseDrag)
        {
            _timelineScrollPos.x -= e.delta.x;
            _timelineScrollPos.y -= e.delta.y;
            e.Use();
            Repaint();
            return;
        }

        if (_isPanningTimeline && e.type == EventType.MouseUp)
        {
            _isPanningTimeline = false;
            e.Use();
        }
    }

    private void ApplyTimelineZoom(Rect viewportRect, Event e)
    {
        float oldZoom = _timelineZoom;
        float newZoom = Mathf.Clamp(oldZoom - e.delta.y * 0.15f, 1f, 10f);

        if (!Mathf.Approximately(newZoom, oldZoom))
        {
            float oldContentWidth = viewportRect.width * oldZoom;
            float mouseContentX = (e.mousePosition.x - viewportRect.x) + _timelineScrollPos.x;
            float fraction = oldContentWidth > 0f ? mouseContentX / oldContentWidth : 0f;

            _timelineZoom = newZoom;
            float newContentWidth = viewportRect.width * newZoom;
            _timelineScrollPos.x = fraction * newContentWidth - (e.mousePosition.x - viewportRect.x);
        }

        e.Use();
        Repaint();
    }

    /// <summary>Ruler ticks every 10%, each labeled with elapsed seconds and the equivalent frame number.</summary>
    private void DrawTimelineRuler(Rect trackRect)
    {
        var rulerLabelStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
        rulerLabelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);

        bool haveClipInfo = animationClip != null && animationClip.length > 0f;
        float frameRate = haveClipInfo ? animationClip.frameRate : 0f;

        for (int i = 0; i <= 10; i++)
        {
            float normalizedT = i / 10f;
            float x = trackRect.x + trackRect.width * normalizedT;
            EditorGUI.DrawRect(new Rect(x, trackRect.y, 1, trackRect.height), new Color(1f, 1f, 1f, 0.08f));

            if (haveClipInfo)
            {
                float seconds = normalizedT * animationClip.length;
                int frame = Mathf.RoundToInt(seconds * frameRate);
                string label = $"{seconds:0.00}s | f{frame}";
                GUI.Label(new Rect(x + 2, trackRect.y + 1, 90, 14), label, rulerLabelStyle);
            }
        }
    }

    private void DrawTimelineBoxes(StagedEventCollection staged, Rect trackRect)
    {
        int[] displayIndices = staged.ComputeDisplayIndices();
        const float collapsedSize = 18f;
        const float expandedWidth = 100f;
        const float expandedHeight = 40f;
        Event e = Event.current;

        var centeredNumberStyle = new GUIStyle(EditorStyles.whiteBoldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };

        // Pass 1: figure out each box's state and rect, and handle input, without drawing
        // yet. Drawing is deferred so the expanded (hovered/dragged) box can be rendered
        // last, on top of its neighbors, regardless of its position in the list.
        var boxes = new List<(Rect rect, Color color, bool expanded, string caption)>(staged.Events.Count);

        // A little headroom at the very top/bottom so lane 0 and lane 1 never clip
        // against the ruler labels or the track edge.
        float laneTop = trackRect.y + 20f;
        float laneBottom = trackRect.yMax - collapsedSize;
        float laneHeight = Mathf.Max(1f, laneBottom - laneTop);

        for (int i = 0; i < staged.Events.Count; i++)
        {
            StagedAnimationEvent evt = staged.Events[i];

            // Fully hidden events skip rendering, hit-testing, and the anchor-time marker
            // line entirely - they're just not part of the timeline while hidden.
            bool hiddenBySoundToggle = hideSoundEvents && evt.FunctionName == "Sound";
            bool hiddenByAmount = evt.CreationOrder < hideEventAmount;
            if (hiddenBySoundToggle || hiddenByAmount)
            {
                continue;
            }

            // The LEFT EDGE of the box is the actual trigger point - this is what the
            // playhead is compared against, and what gets written as the event's time.
            float anchorX = trackRect.x + trackRect.width * evt.NormalizedTime;
            float anchorY = laneTop + laneHeight * evt.LaneOffset;

            // Hover/click always target this fixed small zone at the event's anchor point,
            // regardless of whether the box is currently drawn collapsed or expanded -
            // otherwise the hit target would jump around as the box grows/shrinks.
            var hitRect = new Rect(anchorX, anchorY - collapsedSize / 2f, collapsedSize, collapsedSize);

            bool isSelected = i == _selectedStagedIndex;
            bool isDragging = i == _draggingStagedIndex;
            bool isHovered = hitRect.Contains(e.mousePosition);
            bool isExpanded = isHovered || isDragging;

            Rect boxRect = isExpanded
                ? new Rect(anchorX, anchorY - expandedHeight / 2f, expandedWidth, expandedHeight)
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

            // A thin marker spanning the full track height at the exact anchor time, so
            // the trigger position stays visible regardless of which lane the box is on.
            EditorGUI.DrawRect(new Rect(anchorX - 1, trackRect.y, 2, trackRect.height), new Color(1f, 1f, 1f, 0.25f));

            string caption = isExpanded
                ? $"[{displayIndices[i]}] #{evt.CreationOrder}\n{(evt.FunctionName == "Sound" ? evt.StringParam : evt.FunctionName)}"
                : evt.CreationOrder.ToString();

            boxes.Add((boxRect, boxColor, isExpanded, caption));

            if (e.type == EventType.MouseDown && hitRect.Contains(e.mousePosition))
            {
                _selectedStagedIndex = i;
                _draggingStagedIndex = i;
                LoadFieldsFromStaged(evt);
                e.Use();
                Repaint();
            }
        }

        // Pass 2: draw collapsed boxes first, then expanded ones on top of everything else.
        for (int pass = 0; pass < 2; pass++)
        {
            bool drawingExpandedPass = pass == 1;
            foreach (var box in boxes)
            {
                if (box.expanded != drawingExpandedPass)
                {
                    continue;
                }

                EditorGUI.DrawRect(box.rect, box.color);
                GUI.Box(box.rect, GUIContent.none);
                GUI.Label(box.rect, box.caption, box.expanded ? EditorStyles.whiteMiniLabel : centeredNumberStyle);
            }
        }

        if (_draggingStagedIndex >= 0 && _draggingStagedIndex < staged.Events.Count)
        {
            if (e.type == EventType.MouseDrag)
            {
                StagedAnimationEvent dragged = staged.Events[_draggingStagedIndex];
                dragged.NormalizedTime = Mathf.Clamp01((e.mousePosition.x - trackRect.x) / trackRect.width);
                dragged.LaneOffset = Mathf.Clamp01((e.mousePosition.y - laneTop) / laneHeight);
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

    /// <summary>A small drag handle beneath the timeline so the user can resize its height directly.</summary>
    private void DrawTimelineResizeHandle(Rect viewportRect)
    {
        var handleRect = new Rect(viewportRect.x, viewportRect.yMax, viewportRect.width, 6f);
        EditorGUI.DrawRect(handleRect, new Color(1f, 1f, 1f, 0.12f));
        EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && handleRect.Contains(e.mousePosition))
        {
            _resizingTimelineHeight = true;
            e.Use();
        }
        else if (_resizingTimelineHeight && e.type == EventType.MouseDrag)
        {
            _timelineHeight = Mathf.Clamp(_timelineHeight + e.delta.y, 60f, 500f);
            e.Use();
            Repaint();
        }
        else if (_resizingTimelineHeight && e.type == EventType.MouseUp)
        {
            _resizingTimelineHeight = false;
            e.Use();
        }

        GUILayout.Space(8);
    }

    private void DrawEventInspector(StagedEventCollection staged)
    {
        bool hasSelection = _selectedStagedIndex >= 0 && _selectedStagedIndex < staged.Events.Count;
        GUILayout.Label(hasSelection ? $"Editing Event #{staged.Events[_selectedStagedIndex].CreationOrder}" : "New Event", EditorStyles.boldLabel);

        //selectedFunctionIndex = EditorGUILayout.Popup("Function Name", selectedFunctionIndex, AnimationEventDefinitions.FunctionNames);
        
        //selectedFunctionIndex = Mathf.Clamp(_prefabClipDropdownIndex, 0, clipNames.Length - 1);
        string[] funcName = AnimationEventDefinitions.FunctionNames;
        DrawSearchableDropdownField("Function Name", funcName, selectedFunctionIndex, selected =>
        {
            selectedFunctionIndex = selected;
        }, true, "List of Function names that is triggered during the event by the client");
        
        string functionName = AnimationEventDefinitions.FunctionNames[selectedFunctionIndex];
        bool hasParameter = AnimationEventDefinitions.FunctionsWithParameters.Contains(functionName);

        if (hasParameter)
        {
            DrawAnimationEventParameterFields(functionName);
        }

        showEventConditions = EditorGUILayout.Toggle(new GUIContent(
            "Show Event Conditions",
            "Toggle whether to use conditional check or not." +
            " Conditional check act the same as Animation State Condition." +
            " This essentially will check if a condition is met or not," +
            " if a condition is met, the event will be played, if not" +
            " the event will be skipped."), showEventConditions);
        
        
        if (showEventConditions)
        {
            GUILayout.Label("Conditions", EditorStyles.boldLabel);
            eventConditionIndex = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent(
                    "Condition Index",
                    "Works similarly with Event Collection index." +
                    " Add a Conditional element to the Animation Event." +
                    " Show Event Conditions must be checked for it to be added."), 
                eventConditionIndex));
            DrawConditionFields();
        }

        GUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent(
                    "Add Event",
                    "Add the Animation Event Information" +
                    " That is selected into the Timeline.")))
            {
                StagedAnimationEvent newEvent = BuildStagedEventFromFields(staged.NextCreationOrder++);
                newEvent.NormalizedTime = animationClip != null && animationClip.length > 0f
                    ? _preview.AnimationTime / animationClip.length
                    : 0f;
                staged.Events.Add(newEvent);
                _selectedStagedIndex = staged.Events.Count - 1;
                AddEvent();
                AudioNotification.PlayClick();
            }

            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                if (GUILayout.Button(new GUIContent(
                        "Update Event",
                        "Update the selected Animation Event" +
                        " from the timeline")))
                {
                    ApplyFieldsToStaged(staged.Events[_selectedStagedIndex]);
                    UpdateEvent();
                    AudioNotification.PlayClick();
                }

                if (GUILayout.Button(new GUIContent(
                        "Remove Event",
                        "Remove the selected event" +
                        " from the timeline")))
                {
                    staged.Events.RemoveAt(_selectedStagedIndex);
                    _selectedStagedIndex = -1;
                    RemoveEvent();
                    AudioNotification.PlayRemove();
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
                useContainerSound = EditorGUILayout.Toggle(new GUIContent(
                    "Use Container Sound",
                    "Instead of manually typing the Event name," +
                    " checking this box enables user to select" +
                    " Sound Event Name straight from the container prefab." +
                    " Requires Weapon Container prefab to be inserted"), useContainerSound);
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

        DrawSearchableDropdownField("Sound Event", names, soundEventDropdownIndex, selected =>
        {
            soundEventDropdownIndex = selected;
            paramString = names[selected];
        }, true, "Automatically grabs Event Name from the Weapon Container AdditionalSounds and remove the \"Snd\" prefix" +
                 " when Finalizing Event Timeline");
    }

    /// <summary>
    /// A dropdown-style field backed by <see cref="SearchableStringDropdown"/> instead of
    /// a plain Popup, so long option lists (FBX animation lists, sound-event lists) can
    /// be filtered by typing rather than scrolled through.
    /// </summary>
    private static void DrawSearchableDropdownField(string label, string[] options, int currentIndex, Action<int> onSelected, bool isTooltip, [CanBeNull] string description)
    {
        Rect lineRect = EditorGUILayout.GetControlRect();
        Rect labelRect = new Rect(lineRect.x, lineRect.y, EditorGUIUtility.labelWidth, lineRect.height);
        Rect fieldRect = new Rect(lineRect.x + EditorGUIUtility.labelWidth, lineRect.y, lineRect.width - EditorGUIUtility.labelWidth, lineRect.height);

        EditorGUI.LabelField(labelRect, label);

        string currentLabel = options.Length > 0 && currentIndex >= 0 && currentIndex < options.Length ? options[currentIndex] : "-";

        if (isTooltip)
        {
            if (EditorGUI.DropdownButton(fieldRect, new GUIContent(currentLabel, description), FocusType.Keyboard))
            {
                var dropdown = new SearchableStringDropdown(label, options, onSelected);
                dropdown.Show(fieldRect);
            }
        }
        else
        {
            if (EditorGUI.DropdownButton(fieldRect, new GUIContent(currentLabel), FocusType.Keyboard))
            {
                var dropdown = new SearchableStringDropdown(label, options, onSelected);
                dropdown.Show(fieldRect);
            }
        }
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
        //conditionNameEnum = EditorGUILayout.Popup("Name", conditionNameEnum, names);
        
        DrawSearchableDropdownField("Name", names, conditionNameEnum, selected =>
        {
            conditionNameEnum = selected;
        }, true, "Names for the conditions, will be different depending on Condition Type chosen." +
                 " This is essentially the same as the animation state blending condition");

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

    /// <summary>
    /// Re-reads the current Events Collection Index straight from the static data asset,
    /// discarding whatever is currently staged for it - used by the "Get Event" button
    /// to pull in changes made outside this window (or to undo unsaved timeline edits).
    /// </summary>
    private void ReloadStagedFromStaticData()
    {
        if (staticData == null)
        {
            return;
        }

        List<EventsCollection> eventsCollections = _accessor.GetEventsCollections(staticData);
        if (eventsCollections == null)
        {
            return;
        }

        EnsureListSize(eventsCollections, eventsCollectionIndex + 1, CreateEmptyEventsCollection);
        EventsCollection eventsCollection = eventsCollections[eventsCollectionIndex];

        _stagedCollections[eventsCollectionIndex] = StagedEventCollection.LoadFrom(eventsCollection, _accessor, animationClip);
        _selectedStagedIndex = -1;
        _draggingStagedIndex = -1;
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
            AnimationClip newClip = (AnimationClip)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Animation Clip",
                    "Single Animation clip used" +
                    " to preview animation for inputting Animation Event" +
                    " timing."), 
                animationClip, 
                typeof(AnimationClip), 
                false);
            if (!clipLocked)
            {
                animationClip = newClip;
            }
        }

        using (new EditorGUI.DisabledScope(prefabLocked))
        {
            animationPrefab = (GameObject)EditorGUILayout.ObjectField(new GUIContent(
                "Animation Prefab",
                "This refers to the imported FBX that contains the animation." +
                " An alternative to the singular Animation clip options." +
                " Loads all the imported animation from the FBX prefab" +
                " and lets user choose through the prefab animation" +
                " dropdown menu."), animationPrefab, typeof(GameObject), false);
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
        userPreviewObject = (GameObject)EditorGUILayout.ObjectField(new GUIContent(
            "User Preview Prefab",
            "Prefab for previewing the animation"), userPreviewObject, typeof(GameObject), false);

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

        // The timeline slider stays exactly as before: a 0-1 float driving both the
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
        DrawSearchableDropdownField("Prefab Animation", clipNames, _prefabClipDropdownIndex, selected =>
        {
            _prefabClipDropdownIndex = selected;
            animationClip = _prefabAnimationClips[selected];
        }, false, null);
    }

    private void RefreshContainer()
    {
        Debug.Log("Container and Animation Prefab has been Refreshed!");
    }

    private void RefillEvent()
    {
        Debug.Log("Event Timeline has been refreshed from the static data!");
    }

    private void UpdateEvent()
    {
        Debug.Log("Selected Event has been updated!");
    }

    private void RemoveEvent()
    {
        Debug.LogWarning("Removed Selected Event");
    }

    private void FinalizeTimeline()
    {
        Debug.Log("Timeline Event has been added to the static data!");
    }

    private void AddEvent()
    {
        Debug.Log("Added Event to Staged Timeline!");
    }
}