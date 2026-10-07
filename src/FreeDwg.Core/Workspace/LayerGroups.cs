using System.Text.Json;
using System.Text.Json.Serialization;
using FreeDwg.Core.Scene;

namespace FreeDwg.Core.Workspace;

/// <summary>
/// A folder of layers in the layers panel, switched on and off as one.
/// </summary>
/// <remarks>
/// Members are held as <see cref="Layer"/> objects rather than names or
/// indices: an index moves when a layer before it is deleted, and a name
/// changes when the layer is renamed, but the object is the same one through
/// both and through their undo. Names only appear at the file boundary.
/// </remarks>
public sealed class LayerGroup
{
    public LayerGroup(string name) { Name = name; }

    public string Name { get; set; }

    public List<Layer> Layers { get; } = new();

    /// <summary>
    /// Names the groups file put in this group that this drawing has no layer
    /// for. Kept and written back, so opening a different revision of the
    /// file does not quietly shrink the group for the next one.
    /// </summary>
    public List<string> MissingLayers { get; } = new();

    public bool IsExpanded { get; set; } = true;

    /// <summary>True when every member is on, false when every one is off, null when mixed or empty.</summary>
    public bool? IsOn => LayerGroups.Visibility(Layers);

    public override string ToString() => Name;
}

/// <summary>
/// How the user has sorted a drawing's layers into groups.
/// </summary>
/// <remarks>
/// A view customisation, not drawing data: DWG has no layer folders, so this
/// never touches the command stack and is never written into the drawing. It
/// lives in a file beside it instead -- see <see cref="SidecarPath"/>.
/// </remarks>
public sealed class LayerGroups
{
    public const int FormatVersion = 1;

    public List<LayerGroup> Groups { get; } = new();

    /// <summary>
    /// The groups file for a drawing: <c>plan.dwg</c> keeps its groups in
    /// <c>plan.dwg.freedwg.json</c>. The drawing's extension stays in the
    /// name so a DWG and a DXF of the same name do not share one.
    /// </summary>
    public static string SidecarPath(string drawingPath) => drawingPath + ".freedwg.json";

    public LayerGroup? GroupOf(Layer layer) =>
        Groups.FirstOrDefault(group => group.Layers.Contains(layer));

    /// <summary>
    /// Moves layers into a group, or out of every group when it is null. A
    /// layer is in one group at most. Groups left with nothing in them go.
    /// </summary>
    public void Assign(IEnumerable<Layer> layers, LayerGroup? group)
    {
        foreach (var layer in layers.ToList())
        {
            foreach (var other in Groups)
                if (!ReferenceEquals(other, group)) other.Layers.Remove(layer);

            if (group is not null && !group.Layers.Contains(layer))
                group.Layers.Add(layer);
        }

        Groups.RemoveAll(g => g.Layers.Count == 0 && g.MissingLayers.Count == 0);
    }

    /// <summary>Makes a group of these layers, taking them out of whatever group they were in.</summary>
    public LayerGroup Create(string name, IEnumerable<Layer> layers)
    {
        var group = new LayerGroup(name);
        Groups.Add(group);
        Assign(layers, group);
        return group;
    }

    /// <summary>Removes the group; its layers go back to being ungrouped, untouched.</summary>
    public void Ungroup(LayerGroup group) => Groups.Remove(group);

    public string UniqueName(string stem)
    {
        for (int n = 1; ; n++)
        {
            string name = $"{stem} {n}";
            if (!Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    public static bool? Visibility(IEnumerable<Layer> layers)
    {
        bool anyOn = false, anyOff = false;
        foreach (var layer in layers)
        {
            if (layer.IsOn) anyOn = true; else anyOff = true;
        }

        return anyOn == anyOff ? null : anyOn;
    }

    // ---- the file -----------------------------------------------------------

    /// <summary>
    /// Reads the groups saved beside a drawing, matching layers by name as
    /// DWG does -- without regard to case. A group saved with every layer
    /// off comes back off, which is the point of saving it.
    /// </summary>
    public static LayerGroups FromJson(string json, Drawing drawing)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, Options)
                   ?? throw new JsonException("the file is empty");

        if (file.Version > FormatVersion)
            throw new JsonException($"written by a newer version (format {file.Version})");

        var byName = new Dictionary<string, Layer>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in drawing.Layers) byName.TryAdd(layer.Name, layer);

        var result = new LayerGroups();
        foreach (var dto in file.LayerGroups ?? [])
        {
            var group = new LayerGroup(dto.Name ?? result.UniqueName("Group"))
            {
                IsExpanded = dto.Expanded ?? true,
            };

            foreach (string name in dto.Layers ?? [])
            {
                if (!byName.TryGetValue(name, out var layer))
                    group.MissingLayers.Add(name);
                else if (result.GroupOf(layer) is null && !group.Layers.Contains(layer))
                    group.Layers.Add(layer);
            }

            if (dto.Visible is bool on)
                foreach (var layer in group.Layers) layer.IsOn = on;

            result.Groups.Add(group);
        }

        return result;
    }

    /// <summary>
    /// Writes the groups under the names their layers have now, so a layer
    /// renamed in the editor is still in its group next time. Visibility is
    /// written only when a group is all one way; a mixed group is left as
    /// the drawing has it.
    /// </summary>
    public string ToJson(Drawing drawing)
    {
        var file = new FileDto
        {
            Version = FormatVersion,
            LayerGroups = Groups.Select(group =>
            {
                // A member deleted from the drawing (and not undone) drops out.
                var present = group.Layers.Where(drawing.Layers.Contains).ToList();
                return new GroupDto
                {
                    Name = group.Name,
                    Visible = Visibility(present),
                    Expanded = group.IsExpanded,
                    Layers = present.Select(layer => layer.Name).Concat(group.MissingLayers).ToList(),
                };
            }).ToList(),
        };

        return JsonSerializer.Serialize(file, Options);
    }

    /// <summary>
    /// The groups saved beside <paramref name="drawing"/>, or none if there
    /// is no file. Throws if there is one and it cannot be read, so the
    /// caller can say so rather than overwrite it.
    /// </summary>
    public static LayerGroups Load(Drawing drawing)
    {
        if (drawing.SourcePath is not { } source) return new LayerGroups();

        string path = SidecarPath(source);
        return File.Exists(path) ? FromJson(File.ReadAllText(path), drawing) : new LayerGroups();
    }

    /// <summary>
    /// Writes the groups file, if there is anything to say. A drawing whose
    /// user never made a group gets no file beside it; one that had groups
    /// and lost them all gets an empty list, so the old ones do not return.
    /// </summary>
    /// <returns>Whether a file was written.</returns>
    public bool Save(Drawing drawing)
    {
        if (drawing.SourcePath is not { } source) return false;

        string path = SidecarPath(source);
        if (Groups.Count == 0 && !File.Exists(path)) return false;

        File.WriteAllText(path, ToJson(drawing));
        return true;
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private sealed class FileDto
    {
        public int Version { get; set; }
        public List<GroupDto>? LayerGroups { get; set; }
    }

    private sealed class GroupDto
    {
        public string? Name { get; set; }
        public bool? Visible { get; set; }
        public bool? Expanded { get; set; }
        public List<string>? Layers { get; set; }
    }
}
