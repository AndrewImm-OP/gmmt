using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Core.Indexing;

/// <summary>
/// Maps resource names to (resource, listIndex) pairs for a single archive.
/// All cross-archive matching uses names, never raw indices.
/// </summary>
public sealed class NameIndex
{
    public Dictionary<string, (UndertaleSprite Res, int Idx)> Sprites { get; } = new();
    public Dictionary<string, (UndertaleSound Res, int Idx)> Sounds { get; } = new();
    public Dictionary<string, (UndertaleCode Res, int Idx)> Code { get; } = new();
    public Dictionary<string, (UndertaleGameObject Res, int Idx)> Objects { get; } = new();
    public Dictionary<string, (UndertaleRoom Res, int Idx)> Rooms { get; } = new();
    public Dictionary<string, (UndertaleScript Res, int Idx)> Scripts { get; } = new();
    public Dictionary<string, (UndertaleBackground Res, int Idx)> Backgrounds { get; } = new();
    public Dictionary<string, (UndertaleFont Res, int Idx)> Fonts { get; } = new();
    public Dictionary<string, (UndertalePath Res, int Idx)> Paths { get; } = new();
    public Dictionary<string, (UndertaleTimeline Res, int Idx)> Timelines { get; } = new();
    public Dictionary<string, (UndertaleShader Res, int Idx)> Shaders { get; } = new();
    public Dictionary<string, (UndertaleExtension Res, int Idx)> Extensions { get; } = new();
    public Dictionary<string, (UndertaleAudioGroup Res, int Idx)> AudioGroups { get; } = new();

    /// <summary>
    /// Variables keyed by "name:instanceType" since name alone is not unique.
    /// </summary>
    public Dictionary<string, (UndertaleVariable Res, int Idx)> Variables { get; } = new();

    /// <summary>
    /// Functions keyed by name.
    /// </summary>
    public Dictionary<string, (UndertaleFunction Res, int Idx)> Functions { get; } = new();

    /// <summary>
    /// String content → list of (UndertaleString, index) since multiple string objects
    /// can have identical content.
    /// </summary>
    public Dictionary<string, List<(UndertaleString Str, int Idx)>> StringsByContent { get; } = new();

    /// <summary>
    /// String object identity → index in data.Strings.
    /// Uses ReferenceEquals semantics.
    /// </summary>
    public Dictionary<UndertaleString, int> StringObjToIndex { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// TPAG item → owner key (e.g. "spr:spr_player:0" or "font:fnt_main:0" or "bg:bg_cave:0").
    /// Used for matching anonymous TPAG items across archives.
    /// </summary>
    public Dictionary<UndertaleTexturePageItem, string> TpagOwners { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Duplicate name warnings generated during indexing.
    /// </summary>
    public List<string> Warnings { get; } = new();

    public UndertaleData Data { get; }

    private NameIndex(UndertaleData data) => Data = data;

    /// <summary>
    /// Build a complete name index for an archive.
    /// </summary>
    public static NameIndex Build(UndertaleData data)
    {
        var idx = new NameIndex(data);

        IndexNamed(data.Sprites, idx.Sprites, "Sprite", idx.Warnings);
        IndexNamed(data.Sounds, idx.Sounds, "Sound", idx.Warnings);
        IndexNamed(data.Code, idx.Code, "Code", idx.Warnings);
        IndexNamed(data.GameObjects, idx.Objects, "Object", idx.Warnings);
        IndexNamed(data.Rooms, idx.Rooms, "Room", idx.Warnings);
        IndexNamed(data.Scripts, idx.Scripts, "Script", idx.Warnings);
        IndexNamed(data.Backgrounds, idx.Backgrounds, "Background", idx.Warnings);
        IndexNamed(data.Fonts, idx.Fonts, "Font", idx.Warnings);
        IndexNamed(data.Paths, idx.Paths, "Path", idx.Warnings);
        IndexNamed(data.Timelines, idx.Timelines, "Timeline", idx.Warnings);
        IndexNamed(data.Shaders, idx.Shaders, "Shader", idx.Warnings);
        IndexNamed(data.Extensions, idx.Extensions, "Extension", idx.Warnings);
        IndexNamed(data.AudioGroups, idx.AudioGroups, "AudioGroup", idx.Warnings);

        IndexVariables(data, idx);
        IndexFunctions(data, idx);
        IndexStrings(data, idx);
        IndexTpagOwners(data, idx);

        return idx;
    }

    /// <summary>
    /// Get all resource names for a given resource type (for compare-names command).
    /// </summary>
    public static SortedSet<string> GetNames<T>(IList<T>? list) where T : UndertaleNamedResource
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (list is null) return names;
        foreach (var item in list)
        {
            if (item.Name?.Content is { } name)
                names.Add(name);
        }
        return names;
    }

    private static void IndexNamed<T>(
        IList<T>? list,
        Dictionary<string, (T, int)> dict,
        string typeName,
        List<string> warnings)
        where T : UndertaleNamedResource
    {
        if (list is null) return;
        for (int i = 0; i < list.Count; i++)
        {
            var name = list[i].Name?.Content;
            if (name is null) continue;
            if (!dict.TryAdd(name, (list[i], i)))
            {
                warnings.Add($"Duplicate {typeName} name '{name}' at index {i} (first at {dict[name].Item2})");
            }
        }
    }

    private static void IndexVariables(UndertaleData data, NameIndex idx)
    {
        if (data.Variables is null) return;
        for (int i = 0; i < data.Variables.Count; i++)
        {
            var v = data.Variables[i];
            var name = v.Name?.Content;
            if (name is null) continue;
            var key = $"{name}:{(int)v.InstanceType}";
            idx.Variables.TryAdd(key, (v, i));
        }
    }

    private static void IndexFunctions(UndertaleData data, NameIndex idx)
    {
        if (data.Functions is null) return;
        for (int i = 0; i < data.Functions.Count; i++)
        {
            var f = data.Functions[i];
            var name = f.Name?.Content;
            if (name is null) continue;
            if (!idx.Functions.TryAdd(name, (f, i)))
            {
                idx.Warnings.Add($"Duplicate Function name '{name}' at index {i}");
            }
        }
    }

    private static void IndexStrings(UndertaleData data, NameIndex idx)
    {
        if (data.Strings is null) return;
        for (int i = 0; i < data.Strings.Count; i++)
        {
            var s = data.Strings[i];
            var content = s.Content ?? "";

            idx.StringObjToIndex[s] = i;

            if (!idx.StringsByContent.TryGetValue(content, out var list))
            {
                list = new List<(UndertaleString, int)>();
                idx.StringsByContent[content] = list;
            }
            list.Add((s, i));
        }
    }

    private static void IndexTpagOwners(UndertaleData data, NameIndex idx)
    {
        if (data.Sprites is not null)
        {
            foreach (var spr in data.Sprites)
            {
                if (spr.Textures is null || spr.Name?.Content is not { } name) continue;
                for (int i = 0; i < spr.Textures.Count; i++)
                {
                    if (spr.Textures[i]?.Texture is { } tpag)
                        idx.TpagOwners.TryAdd(tpag, $"spr:{name}:{i}");
                }
            }
        }

        if (data.Fonts is not null)
        {
            foreach (var font in data.Fonts)
            {
                if (font.Texture is { } tpag && font.Name?.Content is { } name)
                    idx.TpagOwners.TryAdd(tpag, $"font:{name}:0");
            }
        }

        if (data.Backgrounds is not null)
        {
            foreach (var bg in data.Backgrounds)
            {
                if (bg.Texture is { } tpag && bg.Name?.Content is { } name)
                    idx.TpagOwners.TryAdd(tpag, $"bg:{name}:0");
            }
        }
    }
}
