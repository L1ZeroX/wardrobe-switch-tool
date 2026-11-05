#if RISV4_JSON
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace YagihataItems.RadialInventorySystemV4
{
    public static class KaiReferences
    {
        public static int Remap(int value, int from, int to)
        {
            if (value == from) return to;
            if (from < to && value > from && value <= to) return value - 1;
            if (from > to && value >= to && value < from) return value + 1;
            return value;
        }
        static void Map(Avatar data, Func<int, int, IndexPair> transform)
        {
            foreach (var prop in data.Groups.SelectMany(g => g.Props))
            {
                prop.PropSets = prop.PropSets.Where(p => p != null).Select(p => transform(p.GroupIndex, p.PropIndex)).Where(p => p != null).ToList();
                foreach (var parameter in prop.Parameters.Where(p => p != null && p.IsRISParam))
                {
                    var match = Regex.Match(parameter.Name ?? "", @"^RISV4-G(\d+)P(\d+)$");
                    if (!match.Success) continue;
                    var mapped = transform(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
                    parameter.Name = mapped == null ? "" : "RISV4-G" + mapped.GroupIndex + "P" + mapped.PropIndex;
                }
                prop.Parameters.RemoveAll(p => p != null && p.IsRISParam && string.IsNullOrEmpty(p.Name));
            }
        }
        public static void PropMoved(Avatar data, Group group, int from, int to)
        {
            int g = data.Groups.IndexOf(group);
            Map(data, (gi, pi) => new IndexPair { GroupIndex = gi, PropIndex = gi == g ? Remap(pi, from, to) : pi });
        }
        public static void GroupMoved(Avatar data, int from, int to)
        {
            Map(data, (gi, pi) => new IndexPair { GroupIndex = Remap(gi, from, to), PropIndex = pi });
            if (data.MenuMode == RIS.MenuModeType.Basic) RemapExclusive(data, i => Remap(i, from, to));
        }
        static void RemapExclusive(Avatar data, Func<int, int> map)
        {
            data.GetExclusiveMode(RIS.ExclusiveGroupType.Group1);
            data.GetExclusiveDisableClip(RIS.ExclusiveGroupType.Group1);
            var modes = new RIS.ExclusiveModeType[8];
            var clips = new GUIDPathPair<UnityEngine.AnimationClip>[8];
            for (int i = 0; i < 8; i++)
            {
                int mapped = map(i);
                if (mapped >= 0 && mapped < 8) { modes[mapped] = data.ExclusiveModes[i]; clips[mapped] = data.ExclusiveDisableClips[i]; }
            }
            data.ExclusiveModes = modes;
            data.ExclusiveDisableClips = clips;
            foreach (var prop in data.Groups.SelectMany(g => g.Props))
                if (prop.ExclusiveGroup != RIS.ExclusiveGroupType.None) prop.ExclusiveGroup = (RIS.ExclusiveGroupType)map((int)prop.ExclusiveGroup);
        }
        public static void RemoveProp(Avatar data, Group group, int index)
        {
            int g = data.Groups.IndexOf(group);
            Map(data, (gi, pi) => gi == g && pi == index ? null : new IndexPair { GroupIndex = gi, PropIndex = gi == g && pi > index ? pi - 1 : pi });
            group.Props.RemoveAt(index);
        }
        public static void RemoveGroup(Avatar data, int index)
        {
            Map(data, (gi, pi) => gi == index ? null : new IndexPair { GroupIndex = gi > index ? gi - 1 : gi, PropIndex = pi });
            data.Groups.RemoveAt(index);
            if (data.MenuMode == RIS.MenuModeType.Basic) RemapExclusive(data, i => i == index ? -1 : i > index ? i - 1 : i);
        }
    }
}
#endif
