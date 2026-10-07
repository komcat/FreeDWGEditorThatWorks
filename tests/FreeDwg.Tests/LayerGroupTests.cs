using System.IO;
using FreeDwg.Core.Commands;
using FreeDwg.Core.Scene;
using FreeDwg.Core.Styling;
using FreeDwg.Core.Workspace;
using FreeDWGEditorThatWorks.ViewModels;
using FreeDwg.Tests.Rendering;

namespace FreeDwg.Tests;

/// <summary>
/// Folders of layers in the layers panel, switched as one, and the file
/// beside the drawing that keeps them.
/// </summary>
/// <remarks>
/// Groups are not drawing data -- DWG has no layer folders -- so none of this
/// goes near the command stack. What matters is that a group follows its
/// layers by identity while the editor is open, and by name across a save,
/// since names are all a file beside the drawing can refer to.
/// </remarks>
public sealed class LayerGroupTests
{
    private static Drawing DrawingWith(params string[] names)
    {
        var drawing = new Drawing();
        foreach (string name in names) drawing.AddLayer(new Layer(name) { Color = Rgb.White });
        return drawing;
    }

    private static Layer L(Drawing drawing, string name) => drawing.Layers.Single(layer => layer.Name == name);

    [Fact]
    public void ALayerIsInOneGroupAtMost()
    {
        var drawing = DrawingWith("0", "Walls", "Doors", "Grid");
        var groups = new LayerGroups();

        var building = groups.Create("Building", [L(drawing, "Walls"), L(drawing, "Doors")]);
        var other = groups.Create("Other", [L(drawing, "Doors"), L(drawing, "Grid")]);

        Assert.Equal(["Walls"], building.Layers.Select(layer => layer.Name));
        Assert.Equal(["Doors", "Grid"], other.Layers.Select(layer => layer.Name));
        Assert.Same(other, groups.GroupOf(L(drawing, "Doors")));
    }

    [Fact]
    public void AGroupEmptiedByMovingItsLayersOutGoesAway()
    {
        var drawing = DrawingWith("0", "Walls");
        var groups = new LayerGroups();

        groups.Create("Building", [L(drawing, "Walls")]);
        groups.Assign([L(drawing, "Walls")], null);

        Assert.Empty(groups.Groups);
    }

    [Fact]
    public void UngroupingLeavesTheLayersAsTheyWere()
    {
        var drawing = DrawingWith("0", "Walls");
        L(drawing, "Walls").IsOn = false;

        var groups = new LayerGroups();
        var group = groups.Create("Building", [L(drawing, "Walls")]);
        groups.Ungroup(group);

        Assert.Empty(groups.Groups);
        Assert.Null(groups.GroupOf(L(drawing, "Walls")));
        Assert.False(L(drawing, "Walls").IsOn);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, null)]
    public void AGroupIsOnOffOrMixed(bool first, bool second, bool? expected)
    {
        var drawing = DrawingWith("A", "B");
        L(drawing, "A").IsOn = first;
        L(drawing, "B").IsOn = second;

        var group = new LayerGroups().Create("G", drawing.Layers);

        Assert.Equal(expected, group.IsOn);
    }

    [Fact]
    public void TheFileRoundTripsGroupsOrderFoldsAndVisibility()
    {
        var drawing = DrawingWith("0", "Walls", "Doors", "Grid", "Notes");
        var groups = new LayerGroups();

        groups.Create("Building", [L(drawing, "Walls"), L(drawing, "Doors")]);
        var setout = groups.Create("Setting out", [L(drawing, "Grid")]);
        setout.IsExpanded = false;
        L(drawing, "Grid").IsOn = false;

        string json = groups.ToJson(drawing);

        // Opened again: everything back on, as a fresh import would have it.
        var reopened = DrawingWith("0", "Walls", "Doors", "Grid", "Notes");
        var read = LayerGroups.FromJson(json, reopened);

        Assert.Equal(["Building", "Setting out"], read.Groups.Select(group => group.Name));
        Assert.Equal(["Walls", "Doors"], read.Groups[0].Layers.Select(layer => layer.Name));
        Assert.True(read.Groups[0].IsExpanded);
        Assert.False(read.Groups[1].IsExpanded);

        // The hidden group comes back hidden, and nothing else is touched.
        Assert.False(L(reopened, "Grid").IsOn);
        Assert.True(L(reopened, "Notes").IsOn);
        Assert.Null(read.GroupOf(L(reopened, "Notes")));
    }

    [Fact]
    public void AMixedGroupLeavesItsLayersAsTheDrawingHasThem()
    {
        var drawing = DrawingWith("A", "B");
        L(drawing, "B").IsOn = false;
        var groups = new LayerGroups();
        groups.Create("G", drawing.Layers);
        string json = groups.ToJson(drawing);

        var reopened = DrawingWith("A", "B");
        L(reopened, "A").IsOn = false;
        LayerGroups.FromJson(json, reopened);

        // Neither the saved state nor "all on": the file says nothing.
        Assert.False(L(reopened, "A").IsOn);
        Assert.True(L(reopened, "B").IsOn);
    }

    [Fact]
    public void LayersAreMatchedByNameWithoutRegardToCase()
    {
        const string json = """{ "version": 1, "layerGroups": [ { "name": "G", "layers": [ "WALLS" ] } ] }""";
        var drawing = DrawingWith("0", "Walls");

        var read = LayerGroups.FromJson(json, drawing);

        Assert.Same(L(drawing, "Walls"), read.Groups[0].Layers.Single());
    }

    [Fact]
    public void ALayerTheDrawingDoesNotHaveIsKeptForTheNextOneThatDoes()
    {
        // The same groups file, read against a revision of the drawing that
        // has lost a layer, must not forget it on the way back out.
        const string json = """{ "version": 1, "layerGroups": [ { "name": "G", "layers": [ "Walls", "Demolished" ] } ] }""";
        var drawing = DrawingWith("0", "Walls");

        var read = LayerGroups.FromJson(json, drawing);
        var again = LayerGroups.FromJson(read.ToJson(drawing), DrawingWith("0", "Walls", "Demolished"));

        Assert.Equal(["Demolished"], read.Groups[0].MissingLayers);
        Assert.Equal(["Walls", "Demolished"], again.Groups[0].Layers.Select(layer => layer.Name));
    }

    [Fact]
    public void ARenamedLayerStaysInItsGroupAndIsSavedUnderItsNewName()
    {
        var drawing = DrawingWith("0", "Walls");
        var stack = new CommandStack(drawing);
        var groups = new LayerGroups();
        groups.Create("Building", [L(drawing, "Walls")]);

        var properties = LayerProperties.Of(L(drawing, "Walls")) with { Name = "A-WALL" };
        stack.Do(new ChangeLayer(drawing, 1, properties));

        // And back, and forward again: the group holds the layer, not its name.
        stack.Undo();
        Assert.Same(groups.Groups[0], groups.GroupOf(L(drawing, "Walls")));
        stack.Redo();

        var read = LayerGroups.FromJson(groups.ToJson(drawing), DrawingWith("0", "A-WALL"));
        Assert.Equal(["A-WALL"], read.Groups[0].Layers.Select(layer => layer.Name));
    }

    [Fact]
    public void ANewerFormatIsRefusedRatherThanHalfRead()
    {
        const string json = """{ "version": 99, "layerGroups": [] }""";

        Assert.Throws<System.Text.Json.JsonException>(() => LayerGroups.FromJson(json, DrawingWith("0")));
    }

    [Fact]
    public void TheFileSitsBesideTheDrawingAndKeepsItsExtension()
    {
        Assert.Equal(@"C:\jobs\plan.dwg.freedwg.json", LayerGroups.SidecarPath(@"C:\jobs\plan.dwg"));
        Assert.NotEqual(LayerGroups.SidecarPath("plan.dwg"), LayerGroups.SidecarPath("plan.dxf"));
    }

    [Fact]
    public void SavingWritesNothingForADrawingNobodyGrouped()
    {
        string dir = Directory.CreateTempSubdirectory("freedwg-groups-").FullName;
        try
        {
            var drawing = DrawingWith("0", "Walls");
            drawing.SourcePath = Path.Combine(dir, "plan.dwg");
            var groups = new LayerGroups();

            Assert.False(groups.Save(drawing));
            Assert.False(File.Exists(LayerGroups.SidecarPath(drawing.SourcePath)));

            // Once there is a file, losing every group has to be written too,
            // or the old groups come back the next time it is opened.
            var group = groups.Create("G", [L(drawing, "Walls")]);
            Assert.True(groups.Save(drawing));
            groups.Ungroup(group);
            Assert.True(groups.Save(drawing));

            Assert.Empty(LayerGroups.Load(drawing).Groups);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- the header in the panel -------------------------------------------

    [Fact]
    public void TheHeaderCheckboxSwitchesEveryLayerInTheGroupAndRedrawsOnce()
    {
        var drawing = DrawingWith("A", "B");
        var group = new LayerGroups().Create("G", drawing.Layers);

        int redraws = 0, changes = 0;
        StaRenderer.OnSta(() =>
        {
            var header = new LayerGroupItem(group, 0, () => changes++);
            foreach (var (layer, i) in drawing.Layers.Select((layer, i) => (layer, i)))
            {
                var row = new LayerItem(layer, i, 0, () => redraws++) { Group = header };
                header.Members.Add(row);
            }

            header.IsOn = false;
            return 0;
        });

        Assert.All(drawing.Layers, layer => Assert.False(layer.IsOn));
        Assert.Equal(0, redraws);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ClickingAMixedHeaderShowsTheWholeGroup()
    {
        var drawing = DrawingWith("A", "B");
        L(drawing, "B").IsOn = false;
        var group = new LayerGroups().Create("G", drawing.Layers);

        StaRenderer.OnSta(() =>
        {
            var header = new LayerGroupItem(group, 0, () => { });
            foreach (var (layer, i) in drawing.Layers.Select((layer, i) => (layer, i)))
                header.Members.Add(new LayerItem(layer, i, 0, () => { }) { Group = header });

            // What a two-state CheckBox sends when clicked in the third state.
            header.IsOn = false;
            return 0;
        });

        Assert.All(drawing.Layers, layer => Assert.True(layer.IsOn));
    }
}
