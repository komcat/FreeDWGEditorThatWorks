using FreeDwg.Core.Scene;
using FreeDwg.Core.Styling;
using FreeDWGEditorThatWorks.ViewModels;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// The rows in the layers panel, and the box that filters them.
/// </summary>
/// <remarks>
/// One fact holds this together: a row carries the index of the layer it
/// shows, rather than the panel reading a row's position back off the list.
/// With a filter in the way those are different numbers, and using the
/// position would quietly rename, recolour or delete a layer the user was
/// not pointing at. There is nothing to see when that goes wrong until the
/// drawing is already changed.
/// </remarks>
public sealed class LayerPanelTests
{
    /// <summary>Builds rows on an STA thread, since each makes a WPF brush.</summary>
    private static List<LayerItem> Rows(params string[] names) => StaRenderer.OnSta(() =>
        names.Select((name, i) => new LayerItem(
            new Layer(name) { Color = Rgb.White }, i, entityCount: 0, () => { })).ToList());

    [Fact]
    public void ARowKnowsWhichLayerItIsWhateverItsPositionInTheList()
    {
        var rows = Rows("0", "Walls", "Dimensions", "Steel");

        // The rows that survive a filter for "s" are Walls, Dimensions and
        // Steel -- at positions 0, 1 and 2 of what is shown, and layers 1, 2
        // and 3 of the drawing. The panel has to use the second set.
        var shown = rows.Where(row => row.Matches("s")).ToList();

        Assert.Equal(["Walls", "Dimensions", "Steel"], shown.Select(row => row.Name));
        Assert.Equal([1, 2, 3], shown.Select(row => row.Index));
    }

    [Theory]
    [InlineData("", 4, "an empty box hides nothing")]
    [InlineData("   ", 4, "and neither does a box of spaces, once trimmed")]
    [InlineData("struc", 2, "a substring, anywhere in the name")]
    [InlineData("STRUC", 2, "and the case does not matter")]
    [InlineData("_stair", 1, "the middle of a name counts too")]
    [InlineData("zzz", 0, "nothing matching is an empty list, not everything")]
    public void TheFilterMatchesAnyPartOfTheNameWithoutRegardToCase(
        string filter, int expected, string what)
    {
        var rows = Rows("0", "Struc_Section_Steel", "Structural_Section_StairA", "Center");

        int shown = rows.Count(row => row.Matches(filter.Trim()));

        Assert.True(expected == shown, $"{what}: expected {expected} rows, got {shown}");
    }

    [Fact]
    public void FilteringNeverChangesWhichLayerARowPointsAt()
    {
        var rows = Rows("0", "DEFPOINTS", "Center", "Struc_Section_Steel");

        // The same row, found two different ways round, is the same layer.
        var direct = rows.Single(row => row.Name == "Struc_Section_Steel");
        var filtered = rows.Where(row => row.Matches("steel")).Single();

        Assert.Equal(direct.Index, filtered.Index);
        Assert.Equal(3, filtered.Index);
    }
}
