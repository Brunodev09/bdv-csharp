namespace BdvEngine.Gui;

/// <summary>
/// Mutual-exclusion group for <see cref="Toggle"/>s — when one becomes Selected,
/// all others in the group become deselected. Set <see cref="AllowSwitchOff"/> = true
/// to permit "no selection"; default false enforces radio-button semantics.
/// </summary>
public sealed class ToggleGroup
{
    private readonly List<Toggle> _members = new();
    public bool AllowSwitchOff = false;
    public Toggle? Selected { get; private set; }

    public void Register(Toggle t)
    {
        _members.Add(t);
        if (t.Value && Selected == null) Selected = t;
    }

    public void SetSelected(Toggle t)
    {
        Selected = t;
        for (int i = 0; i < _members.Count; i++)
            _members[i].SetValueInternal(_members[i] == t);
    }

    public void Clear()
    {
        if (!AllowSwitchOff) return;
        Selected = null;
        for (int i = 0; i < _members.Count; i++) _members[i].SetValueInternal(false);
    }
}
