using System.Collections.Generic;
using UnityEngine;

namespace Oxipital
{
    /// <summary>A dancer group seen from another one (to follow it as a master), whatever its dancer type.</summary>
    public interface IDancerGroup
    {
        string GroupName { get; }
        int DancerCount { get; }
        Vector3 GetDancerPosition(int index);
        float GetDancerIntensity(int index);
        float GetDancerSize(int index);
        void UpdateDancers();
        float Count { get; }
    }

    /// <summary>Every enabled dancer group, so one can find another by name (master / slave ballets).</summary>
    public static class DancerGroups
    {
        public static readonly List<IDancerGroup> All = new List<IDancerGroup>();

        /// <summary>The enabled group of that name, null if none (or if it is the asking group itself). Reads the names:
        /// call it on a change, not every frame.</summary>
        public static IDancerGroup Find(string name, IDancerGroup self)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var g = All[i];
                if (g != self && (g as Object) != null && g.GroupName == name) return g;
            }
            return null;
        }
    }

    /// <summary>Shows a string field as a dropdown of the dancer groups of the open scenes (None first).</summary>
    public class BalletNameAttribute : PropertyAttribute
    {
    }
}
