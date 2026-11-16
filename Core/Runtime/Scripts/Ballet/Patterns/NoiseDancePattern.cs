using UnityEngine;

namespace Oxipital
{
    /// <summary>
    /// Brownian wandering of each dancer around the place the other patterns give it (any dancer group: orbs, forces,
    /// lights). Applied on top of them, after the group has scaled them by patternSize (amplitude is in group local
    /// units). Its own clock (Time.deltaTime x frequency), so it moves even when the patterns stand still, and a
    /// seed, so a Recorder render repeats. Put it after the other patterns on the group.
    /// </summary>
    public class NoiseDancePattern : DancePattern
    {
        [Tooltip("How far each dancer wanders (group local units).")]
        [Range(0f, 20f)] public float amplitude = 1f;
        [Tooltip("How fast it wanders (Hz).")]
        [Range(0f, 5f)] public float frequency = 0.2f;
        [Tooltip("Wandering on each axis (group local): 0 = none on that axis.")]
        public Vector3 axes = Vector3.one;
        public int seed = 0;

        float _time;

        public override bool AfterSize => true;

        public override void updatePattern<T>(DancerGroup<T> group)
        {
            _time += Time.deltaTime * frequency;
            if (group.items.Count == 0 || weight == 0f || amplitude == 0f) return;
            float smoothWeight = Mathf.Sin(weight * Mathf.PI - Mathf.PI / 2) * 0.5f + 0.5f;
            for (int i = 0; i < group.items.Count; i++)
            {
                Dancer dancer = group.items[i];
                if (dancer == null) continue;
                float o = seed * 17.31f + i * 7.13f;
                var offset = new Vector3(Fbm(o) * axes.x, Fbm(o + 31.7f) * axes.y, Fbm(o + 63.1f) * axes.z);
                dancer.transform.localPosition += offset * (amplitude * smoothWeight);
            }
        }

        // Fractal noise in about -1..1
        float Fbm(float offset)
        {
            float t = _time;
            return ((Mathf.PerlinNoise(t, offset) * 2f - 1f)
                  + 0.5f * (Mathf.PerlinNoise(t * 2.03f, offset + 11.1f) * 2f - 1f)) / 1.5f;
        }
    }
}
