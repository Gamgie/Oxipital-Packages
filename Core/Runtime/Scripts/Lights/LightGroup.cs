using UnityEngine;
using UnityEngine.Rendering;
using OSCQuery;

namespace Oxipital
{
    /// <summary>
    /// A ballet of lights: N HDRP lights (point or spot) placed by the group's dance patterns like any dancer group,
    /// each shown by a glowing mesh (a sphere by default). Movement and orientation belong to the dance patterns and the
    /// group (a NoiseDancePattern for wandering; dancerLookAt, dancerLookAtMode and dancerRotation for where the lights
    /// point, blended so a change never jumps). Their intensity is the product of every effect, so they stack: the
    /// group's dancerIntensity and count fade, an oscillation (wave, Noise among them, depth, speed, phase delay between
    /// dancers) and a beat (bpm: each dancer flashes for flashLength of each flash period, beatDelay between dancers),
    /// plus a colour noise. Time is accumulated from Time.deltaTime and the noise is seeded, so a Recorder render
    /// repeats. Nothing is allocated per frame.
    /// </summary>
    public class LightGroup : DancerGroup<LightDancer>
    {
        public enum Shape { Point, Spot }
        public enum Wave { Constant, Sine, Triangle, Saw, Square, Noise }

        [Space(20)]
        [Header("Light")]
        public Shape shape = Shape.Point;
        [ColorUsage(false)]
        public Color color = new Color(1f, 0.55f, 0.3f);
        [Tooltip("Luminous power of each light (lumen), before the effects.")]
        [Range(0, 200000)] public float intensity = 8000f;
        [Tooltip("Distance the light reaches (m).")]
        [Range(0.1f, 500f)] public float range = 30f;
        [Tooltip("Spot cone angle (degrees).")]
        [Range(1f, 179f)] public float spotAngle = 60f;
        [Tooltip("Spot edge: 0 = sharp, 1 = fades from the axis.")]
        [Range(0f, 1f)] public float spotSoftness = 0.5f;
        [Tooltip("Size of the emitting sphere (m): softer shadows and highlights.")]
        [Range(0f, 5f)] public float lightRadius = 0.2f;
        [Tooltip("The light shows in the volumetric fog (light shafts, halos).")]
        public bool volumetric = true;
        [Range(0f, 16f)] public float volumetricIntensity = 1f;
        public bool shadows = false;

        [Header("Mesh")]
        public bool showMesh = true;
        [Tooltip("Mesh size, relative to the dancer size.")]
        [Range(0f, 10f)] public float meshSize = 0.3f;
        [Tooltip("Mesh brightness for the light's colour; it follows every effect.")]
        [Range(0f, 100f)] public float meshEmission = 4f;
        [Tooltip("Any mesh instead of the sphere.")]
        [DoNotExpose] public Mesh mesh;

        [Header("Oscillation")]
        public Wave wave = Wave.Constant;
        [Tooltip("How much the wave lowers the intensity: 0 = none, 1 = down to black.")]
        [Range(0f, 1f)] public float waveDepth = 1f;
        [Tooltip("Cycles per second, or per beat when bpm is set.")]
        [Range(0f, 10f)] public float waveSpeed = 0.5f;
        [Tooltip("Phase shift between one dancer and the next (fraction of a cycle).")]
        [Range(0f, 1f)] public float wavePhaseDelay = 0f;

        [Header("Beat")]
        [Tooltip("Beats per minute. 0 = off: the lights stay on (other effects apart).")]
        [Range(0f, 300f)] public float bpm = 0f;
        [Tooltip("Beats between two flashes.")]
        [Range(0.25f, 16f)] public float beatsPerFlash = 1f;
        [Tooltip("How long a light stays on, as a fraction of the flash period.")]
        [Range(0f, 1f)] public float flashLength = 0.5f;
        [Tooltip("Fade in and out, as a fraction of the time on.")]
        [Range(0f, 1f)] public float flashSoftness = 0.2f;
        [Tooltip("Delay between one dancer and the next, as a fraction of the flash period: a chase.")]
        [Range(0f, 1f)] public float beatDelay = 0f;

        [Header("Colour noise")]
        [Tooltip("Hue and brightness variation of each light.")]
        [Range(0f, 1f)] public float colorNoise = 0f;
        [Range(0f, 5f)] public float noiseSpeed = 0.3f;
        public int seed = 0;

        static readonly int UnlitColorId = Shader.PropertyToID("_UnlitColor");
        static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");

        float _time;

        public LightGroup() : base("Light")
        {
        }

        protected override void Update()
        {
            base.Update();
            float dt = Time.deltaTime;
            _time += dt;

            int n = items.Count;
            for (int i = 0; i < n; i++)
            {
                LightDancer d = items[i];
                if (d == null || d.lightSource == null) continue;
                float seedA = seed * 17.31f + i * 7.13f;
                float nt = _time * noiseSpeed;

                // ---- intensity: every effect multiplies
                float fade = d.intensity * (1f - d.killProgress);
                float level = fade * WaveFactor(i) * BeatFactor(i);

                Color c = color;
                if (colorNoise > 0f)
                {
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    h = Mathf.Repeat(h + 0.25f * colorNoise * Noise(nt, seedA + 123.4f), 1f);
                    v *= 1f + 0.5f * colorNoise * Noise(nt, seedA + 157.9f);
                    c = Color.HSVToRGB(h, s, v);
                }

                // The light points where the dancer points (dancerLookAt, dancerLookAtMode, dancerRotation of the group)
                Transform lt = d.lightSource.transform;
                if (lt.localRotation != Quaternion.identity) lt.localRotation = Quaternion.identity;

                ApplyLight(d, c, intensity * level);
                ApplyMesh(d, c, level);
            }
        }

        void ApplyLight(LightDancer d, Color c, float lumen)
        {
            Light l = d.lightSource;
            bool on = lumen > 0.01f;
            if (l.enabled != on) l.enabled = on;
            if (!on) return;
            LightType type = shape == Shape.Spot ? LightType.Spot : LightType.Point;
            if (l.type != type) l.type = type;
            if (l.lightUnit != LightUnit.Lumen) l.lightUnit = LightUnit.Lumen;
            l.color = c;
            l.intensity = lumen;
            if (l.range != range) l.range = range;
            if (type == LightType.Spot)
            {
                if (l.spotAngle != spotAngle) l.spotAngle = spotAngle;
                float inner = spotAngle * (1f - spotSoftness);
                if (l.innerSpotAngle != inner) l.innerSpotAngle = inner;
            }
            LightShadows wantShadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (l.shadows != wantShadows) l.shadows = wantShadows;
            var hd = d.lightData;
            if (hd != null)
            {
                if (hd.affectsVolumetric != volumetric) hd.affectsVolumetric = volumetric;
                if (hd.volumetricDimmer != volumetricIntensity) hd.volumetricDimmer = volumetricIntensity;
                if (hd.shapeRadius != lightRadius) hd.shapeRadius = lightRadius;
            }
        }

        void ApplyMesh(LightDancer d, Color c, float level)
        {
            MeshRenderer r = d.meshRenderer;
            if (r == null) return;
            bool visible = showMesh && meshSize > 0f;
            if (r.enabled != visible) r.enabled = visible;
            if (!visible) return;
            Mesh m = mesh != null ? mesh : d.defaultMesh;
            if (d.meshFilter != null && d.meshFilter.sharedMesh != m) d.meshFilter.sharedMesh = m;
            Transform mt = r.transform;
            Vector3 scale = new Vector3(meshSize, meshSize, meshSize);
            if (mt.localScale != scale) mt.localScale = scale;
            Color glow = c.linear * (meshEmission * level);
            glow.a = 1f;
            d.block.SetColor(UnlitColorId, glow);
            d.block.SetColor(EmissiveColorId, glow);
            r.SetPropertyBlock(d.block);
        }

        // 0..1 multiplier of the oscillation
        float WaveFactor(int i)
        {
            if (wave == Wave.Constant || waveDepth <= 0f) return 1f;
            float speed = bpm > 0f ? waveSpeed * bpm / 60f : waveSpeed;
            float p = _time * speed - i * wavePhaseDelay;
            float v;
            switch (wave)
            {
                case Wave.Sine: v = 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 2f); break;
                case Wave.Triangle: v = 1f - Mathf.Abs(Mathf.Repeat(p, 1f) * 2f - 1f); break;
                case Wave.Saw: v = 1f - Mathf.Repeat(p, 1f); break;
                case Wave.Square: v = Mathf.Repeat(p, 1f) < 0.5f ? 1f : 0f; break;
                default: v = 0.5f + 0.5f * Noise(p, seed * 5.7f + i * 3.3f); break;
            }
            return 1f - waveDepth * (1f - v);
        }

        // 0..1 envelope of the beat flashes (1 without bpm)
        float BeatFactor(int i)
        {
            if (bpm <= 0f) return 1f;
            if (flashLength <= 0f) return 0f;
            float f = Mathf.Repeat(_time * bpm / 60f / beatsPerFlash - i * beatDelay, 1f);
            if (f >= flashLength) return 0f;
            float soft = flashLength * flashSoftness * 0.5f;
            if (soft <= 0f) return 1f;
            return Mathf.Clamp01(f / soft) * Mathf.Clamp01((flashLength - f) / soft);
        }

        // Smooth noise in -1..1
        static float Noise(float t, float offset) => Mathf.PerlinNoise(t, offset) * 2f - 1f;

        /// <summary>Restart the oscillation and the beat from their start (for example on a downbeat).</summary>
        public void ResetBeat()
        {
            _time = 0f;
        }
    }
}
