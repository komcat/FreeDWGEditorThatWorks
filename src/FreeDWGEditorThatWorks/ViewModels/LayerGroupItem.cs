using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FreeDwg.Core.Workspace;

namespace FreeDWGEditorThatWorks.ViewModels;

/// <summary>
/// The header of one group in the layers panel: its name, how many layers it
/// holds, and one checkbox for all of them.
/// </summary>
/// <remarks>
/// The rows are grouped by <see cref="LayerItem.Group"/>, which is one of
/// these, so the list itself stays the flat set of layer rows it always was
/// and every row still carries its own layer index. Layers in no group sit
/// under a header with no <see cref="Group"/> and no checkbox: their on/off
/// is not something the groups file keeps, and a checkbox there would look
/// as if it were.
/// </remarks>
public sealed class LayerGroupItem : INotifyPropertyChanged
{
    private readonly Action _changed;

    public LayerGroupItem(LayerGroup? group, int order, Action changed)
    {
        Group = group;
        Order = order;
        _changed = changed;
    }

    /// <summary>The group shown, or null for the layers in none.</summary>
    public LayerGroup? Group { get; }

    public bool IsGroup => Group is not null;

    /// <summary>Where the group sits in the panel; the ungrouped come last.</summary>
    public int Order { get; }

    public List<LayerItem> Members { get; } = new();

    public string Name => Group?.Name ?? "Other layers";

    public string CountText => Members.Count.ToString();

    /// <summary>
    /// On when every member is, off when none is, and the third state when
    /// they disagree. Ticking it puts them all the same way.
    /// </summary>
    public bool? IsOn
    {
        get
        {
            bool anyOn = Members.Any(row => row.IsOn), anyOff = Members.Any(row => !row.IsOn);
            return anyOn == anyOff ? null : anyOn;
        }
        set
        {
            // The third state is not something to choose, only to be in; a
            // click out of it means "show them", which is the safe way round.
            // A two-state checkbox clicked while mixed sends false, so the
            // current state decides rather than the value.
            bool on = IsOn is null || value != false;
            foreach (var row in Members) row.SetOnQuietly(on);

            OnPropertyChanged();
            _changed();
        }
    }

    public bool IsExpanded
    {
        get => Group?.IsExpanded ?? true;
        set
        {
            if (Group is null || Group.IsExpanded == value) return;
            Group.IsExpanded = value;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>A member was switched on its own row; the header may be mixed now.</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsOn));

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
