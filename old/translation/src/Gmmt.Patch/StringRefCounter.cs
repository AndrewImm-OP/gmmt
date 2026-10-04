using System.Runtime.CompilerServices;
using UndertaleModLib;
using UndertaleModLib.Models;

namespace Gmmt.Patch;

public static class StringRefCounter
{
    public static Dictionary<UndertaleString, int> CountAllReferences(UndertaleData data)
    {
        var counts = new Dictionary<UndertaleString, int>(UndertaleStringIdentityComparer.Instance);

        // Count code instruction string references
        if (data.Code is not null)
        {
            foreach (var code in data.Code)
            {
                if (code.Instructions is null)
                    continue;

                foreach (var instr in code.Instructions)
                {
                    var str = instr.ValueString?.Resource;
                    if (str is not null)
                    {
                        Increment(counts, str);
                    }
                }
            }
        }

        // Count named resource Name fields
        CountNamedResources(counts, data.Sounds);
        CountNamedResources(counts, data.Sprites);
        CountNamedResources(counts, data.Backgrounds);
        CountNamedResources(counts, data.Paths);
        CountNamedResources(counts, data.Scripts);
        CountNamedResources(counts, data.Fonts);
        CountNamedResources(counts, data.GameObjects);
        CountNamedResources(counts, data.Rooms);
        CountNamedResources(counts, data.Extensions);
        CountNamedResources(counts, data.Shaders);
        CountNamedResources(counts, data.Timelines);
        CountNamedResources(counts, data.Code);
        CountNamedResources(counts, data.Functions);
        CountNamedResources(counts, data.AudioGroups);

        // Count extra string fields for sounds
        if (data.Sounds is not null)
        {
            foreach (var sound in data.Sounds)
            {
                if (sound.Type is not null)
                    Increment(counts, sound.Type);
                if (sound.File is not null)
                    Increment(counts, sound.File);
            }
        }

        return counts;
    }

    private static void CountNamedResources<T>(Dictionary<UndertaleString, int> counts, IList<T>? resources)
        where T : UndertaleNamedResource
    {
        if (resources is null)
            return;

        foreach (var resource in resources)
        {
            if (resource.Name is not null)
            {
                Increment(counts, resource.Name);
            }
        }
    }

    private static void Increment(Dictionary<UndertaleString, int> counts, UndertaleString str)
    {
        counts.TryGetValue(str, out var count);
        counts[str] = count + 1;
    }

    internal sealed class UndertaleStringIdentityComparer : IEqualityComparer<UndertaleString>
    {
        public static readonly UndertaleStringIdentityComparer Instance = new();

        private UndertaleStringIdentityComparer() { }

        public bool Equals(UndertaleString? x, UndertaleString? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(UndertaleString obj)
            => RuntimeHelpers.GetHashCode(obj);
    }
}
