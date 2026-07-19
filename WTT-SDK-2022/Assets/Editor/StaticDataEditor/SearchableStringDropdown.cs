using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// A searchable string picker, used anywhere a plain Popup would otherwise force
/// scrolling through a long flat list (the FBX/prefab animation list, and the
/// Container's sound-event list). Built on Unity's own AdvancedDropdown, which
/// supplies the search field and filtering for free - the same control Unity's
/// "Add Component" menu uses.
/// </summary>
internal sealed class SearchableStringDropdown : AdvancedDropdown
{
    private readonly string _title;
    private readonly string[] _options;
    private readonly Action<int> _onSelect;

    public SearchableStringDropdown(string title, string[] options, Action<int> onSelect)
        : base(new AdvancedDropdownState())
    {
        _title = title;
        _options = options;
        _onSelect = onSelect;
        minimumSize = new Vector2(220f, 300f);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem(_title);
        for (int i = 0; i < _options.Length; i++)
        {
            root.AddChild(new AdvancedDropdownItem(_options[i]) { id = i });
        }
        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        _onSelect?.Invoke(item.id);
    }
}
