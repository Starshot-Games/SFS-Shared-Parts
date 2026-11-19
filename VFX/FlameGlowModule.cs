using System;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SFS.Parts.Modules
{
    // The light the plume throws around it, drawn by the FlameGlow shader from the flame's own shape and brightness
    public class FlameGlowModule : MonoBehaviour
    {
        public const int Slabs = 16; // glowSlabs in the shader
        public const int Highlights = 3; // glowHighlights in the shader

        static readonly int
            GlowRect = Shader.PropertyToID("glowRect"),
            GlowMargin = Shader.PropertyToID("glowMargin"),
            GlowSlabAxis = Shader.PropertyToID("glowSlabAxis"),
            GlowSlabShape = Shader.PropertyToID("glowSlabShape"),
            GlowSlabColor = Shader.PropertyToID("glowSlabColor"),
            GlowSpread = Shader.PropertyToID("glowSpread"),
            GlowExitAxis = Shader.PropertyToID("glowExitAxis"),
            GlowExitShape = Shader.PropertyToID("glowExitShape"),
            GlowExitColor = Shader.PropertyToID("glowExitColor"),
            GlowColor = Shader.PropertyToID("glowColor"),
            GlowFalloff = Shader.PropertyToID("glowFalloff"),
            GlowOverlap = Shader.PropertyToID("glowOverlap"),
            GlowComposited = Shader.PropertyToID("glowComposited"),
            GlowNozzle = Shader.PropertyToID("glowNozzle"),
            GlowHighlightRect = Shader.PropertyToID("glowHighlightRect"),
            GlowHighlightUV = Shader.PropertyToID("glowHighlightUV"),
            GlowHighlightColor = Shader.PropertyToID("glowHighlightColor"),
            HighlightTex = Shader.PropertyToID("_HighlightTex");

        const float ThroatEmission = 6.31f; // GetEmission in the flame shader at the nozzle, 10^0.8
        const float Reach = 3; // how many of the widest spread's radii the glow is drawn out to past the plume
        const float EddyTiles = 1.5f; // noise tiles per plume length the patches of brighter and dimmer light are laid down it at
        const float EddyGrowth = 0.35f; // of the plume's length it takes them to build up from the nozzle, eddyGrowth in the flame shader
        const float ExitDepth = 0.6f; // of the nozzle's half-width, how far down the plume the exit's light comes from

        public bool vac;

        // Debug preview - refreshes on validate. EngineEffects drives this at runtime.
        #if UNITY_EDITOR
        [BoxGroup("Debug"), Range(0, 1)] public float debugThrottle, debugVacuum;
        #endif

        [Required] public FlameMeshModule flame;
        [Required] public MeshRenderer glow;

        // Data
        [Space]
        public AnimationCurve throttleResponseCurve = AnimationCurve.Linear(0, 0, 1, 1);
        public AnimationCurve vacuumResponseCurve = AnimationCurve.Linear(0, 0, 1, 1);
        [Tooltip("How much the glow's brightness flickers with the flame's")]
        [Range(0, 0.5f)] public float flicker = 0.1f;
        [Tooltip("How much brighter and dimmer patches of it stream down the plume with the flame's eddies")]
        [Range(0, 0.5f)] public float turbulence = 0.1f;
        [Tooltip("Moves the nozzle exit's glow along the nozzle's Y axis, in nozzle half-widths: above 0 up into the bell, below 0 down the plume")]
        public float exitOffset;
        [Space]
        [HideIf(nameof(vac))] public GlowData groundData = new() { color = Color.white, bloom = 7, bloomRadius = 2.5f, halo = 2, haloRadius = 7, exit = 0.25f, exitRadius = 2.5f, exitColor = new Color(1, 0.75f, 0.5f), afterglow = 0.3f, afterglowLength = 0.2f, afterglowHold = 0.3f, rolloff = 6 };
        public GlowData vacuumData = new() { color = Color.white, bloom = 2.5f, bloomRadius = 3, haloRadius = 8, exit = 3, exitRadius = 2.2f, exitColor = new Color(1, 0.75f, 0.5f) };

        // Always sent whole: a property block fixes an array's length the first time it is set
        static readonly Vector4[] slabAxis = new Vector4[Slabs];
        static readonly Vector4[] slabShape = new Vector4[Slabs];
        static readonly Vector4[] slabColor = new Vector4[Slabs];
        static readonly Vector4[] highlightRect = new Vector4[Highlights];
        static readonly Vector4[] highlightUV = new Vector4[Highlights];
        static readonly Vector4[] highlightColor = new Vector4[Highlights];

        MaterialPropertyBlock propertyBlock;
        MeshFilter meshFilter;
        float eddySeed;
        void Awake() => eddySeed = Random.value * 100;
        void OnDestroy() => FlameGlowCompositor.Remove(this);

        // What FlameGlowCompositor draws this glow's light with
        internal MaterialPropertyBlock PropertyBlock => propertyBlock;
        internal Mesh Mesh => glow != null && (meshFilter != null || glow.TryGetComponent(out meshFilter)) ? meshFilter.sharedMesh : null;


        // Driven by EngineEffects, after the flames - it gathers throttle/vacuum once and pushes them in here.
        // visibility is its flame's: under 1 while a brighter flame burning alongside drowns it out.
        public void Apply(float throttle, float vacuum, float visibility = 1)
        {
            if (glow == null || flame == null)
                return;

            float adjThrottle = throttleResponseCurve.Evaluate(throttle);
            float adjVacuum = vac ? 1 : vacuumResponseCurve.Evaluate(vacuum);

            // Fully disable the glow when the engine is off, rather than only fading it
            bool on = adjThrottle > 0 && flame.appliedThrottle > 0;
            if (glow.gameObject.activeSelf != on)
                glow.gameObject.SetActive(on);

            if (!on)
                return;

            GlowData data = vac ? vacuumData : GlowData.Lerp(groundData, vacuumData, adjVacuum);

            Transform space = glow.transform;
            float toLocal = 1 / Mathf.Max(space.TransformVector(Vector3.right).magnitude, 1e-6f);
            float nozzle = flame.GetNozzleRadius(false) * toLocal;
            float bloomRadius = data.bloomRadius * nozzle;
            float haloRadius = data.haloRadius * nozzle;
            float exitRadius = data.exitRadius * nozzle;

            // Slabs packed closer at the nozzle, where the plume is brightest and narrowest, on past where it fades into its afterglow
            bool merging = flame.merge.amount > 0;
            float fadedOut = merging ? Mathf.Max(flame.GetFadedOutMeshY(false), flame.GetFadedOutMeshY(true)) : flame.GetFadedOutMeshY(false);
            float end = fadedOut * (1 + data.afterglowLength);
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            Vector2 start = GetCentre(0, space);
            Vector2 nozzleCentre = start;
            float eddyStream = Time.time * flame.GetEddyFlow(); // plume lengths the eddies have streamed
            Vector2 nozzleDown = Vector2.down;
            float nozzleLight = 0;

            for (int i = 0; i < Slabs; i++)
            {
                float t = (i + 1f) / Slabs;
                Vector2 stop = GetCentre(end * t * t, space);
                Vector2 down = stop - start;
                float length = down.magnitude;
                down = length > 1e-6f ? down / length : Vector2.down;

                t = (i + 0.5f) / Slabs;
                GetSlab(end * t * t, space, toLocal, out float halfWidth, out float light);
                Vector2 middle = (start + stop) * 0.5f;

                slabColor[i] = flame.FlameColor;

                // The glow goes by how bright the flame looks, which saturates as its light climbs, so the white-hot core doesn't outshine the rest
                if (data.rolloff > 0)
                    light = (1 - Mathf.Exp(-data.rolloff * light)) / data.rolloff;

                // The hot exhaust glows on down the flame and a little past it, lit by it, after the flame's own light has faded
                if (i == 0)
                {
                    nozzleDown = down;
                    nozzleLight = light;
                }
                float holdFrom = fadedOut * data.afterglowHold;
                float cooled = 1 - Mathf.SmoothStep(0, 1, (end * t * t - holdFrom) / Mathf.Max(end - holdFrom, 1e-4f));
                light = Mathf.Max(light, data.afterglow * nozzleLight * cooled);
                light *= GetEddies(end * t * t, eddyStream);

                slabAxis[i] = new Vector4(middle.x, middle.y, down.x, down.y);
                slabShape[i] = new Vector4(halfWidth, length, light);

                Vector2 extent = new Vector2(Mathf.Abs(down.y), Mathf.Abs(down.x)) * halfWidth + new Vector2(Mathf.Abs(down.x), Mathf.Abs(down.y)) * (length * 0.5f);
                min = Vector2.Min(min, middle - extent);
                max = Vector2.Max(max, middle + extent);
                start = stop;
            }

            // The nozzle exit: the inside of the bell and the chamber seen through it, far hotter than any of the plume
            float exitHalfWidth = nozzle;
            float exitLength = ExitDepth * nozzle;
            float exitLight = data.exit * 2 * Mathf.PI * exitRadius * exitRadius / (2 * exitHalfWidth * exitLength);
            Vector2 exitCentre = nozzleCentre + nozzleDown * (exitLength * 0.5f - exitOffset * nozzle);
            min = Vector2.Min(min, exitCentre - Vector2.one * (exitHalfWidth + exitLength));
            max = Vector2.Max(max, exitCentre + Vector2.one * (exitHalfWidth + exitLength));

            float margin = Reach * Mathf.Max(Mathf.Max(bloomRadius, exitRadius), data.halo > 0 ? haloRadius : 0);
            min -= Vector2.one * margin;
            max += Vector2.one * margin;

            // The highlights at the nozzle, each matched up with the vacuum one at its index
            Highlight[] fromHighlights = vac ? vacuumData.highlights : groundData.highlights;
            Highlight[] toHighlights = vacuumData.highlights;
            Texture highlightTexture = null;
            for (int i = 0; i < Highlights; i++)
            {
                highlightRect[i] = new Vector4(0, 0, 1, 1);
                highlightUV[i] = Vector4.zero;
                highlightColor[i] = Vector4.zero;

                if (fromHighlights == null || toHighlights == null || i >= fromHighlights.Length || i >= toHighlights.Length || fromHighlights[i].sprite == null)
                    continue;

                Highlight a = fromHighlights[i], b = toHighlights[i];
                Sprite sprite = a.sprite;
                Vector2 position = Vector2.Lerp(a.position, b.position, adjVacuum) * nozzle;
                Vector2 scale = Vector2.Lerp(a.scale, b.scale, adjVacuum) * nozzle;
                Rect rect = sprite.rect;
                Vector2 size = rect.size / sprite.pixelsPerUnit;
                Vector2 pivot = sprite.pivot / rect.size;
                Vector2 low = position - Vector2.Scale(scale, Vector2.Scale(pivot, size));
                Vector2 high = position + Vector2.Scale(scale, Vector2.Scale(Vector2.one - pivot, size));
                Vector2 texel = new Vector2(1f / sprite.texture.width, 1f / sprite.texture.height);

                highlightRect[i] = new Vector4(low.x, low.y, high.x, high.y);
                highlightUV[i] = new Vector4(rect.xMin * texel.x, rect.yMin * texel.y, rect.xMax * texel.x, rect.yMax * texel.y);
                highlightColor[i] = Color.Lerp(a.color, b.color, adjVacuum);
                highlightTexture = sprite.texture;

                // In the nozzle's frame (across, up from its exit); the bounds only need to take in its farthest corner
                float reach = Mathf.Max(Mathf.Max(Mathf.Abs(low.x), Mathf.Abs(high.x)), Mathf.Max(Mathf.Abs(low.y), Mathf.Abs(high.y)));
                min = Vector2.Min(min, nozzleCentre - Vector2.one * reach);
                max = Vector2.Max(max, nozzleCentre + Vector2.one * reach);
            }

            float flickering = Mathf.Max(1 + flicker * flame.FlickerNoise, 0);
            Color color = data.color;
            color.a = adjThrottle * visibility * flickering;

            propertyBlock ??= new MaterialPropertyBlock();
            glow.GetPropertyBlock(propertyBlock);
            propertyBlock.SetVector(GlowRect, new Vector4(min.x, min.y, max.x, max.y));
            propertyBlock.SetFloat(GlowMargin, margin);
            propertyBlock.SetVectorArray(GlowSlabAxis, slabAxis);
            propertyBlock.SetVectorArray(GlowSlabShape, slabShape);
            propertyBlock.SetVectorArray(GlowSlabColor, slabColor);
            propertyBlock.SetVector(GlowSpread, new Vector4(data.bloom, bloomRadius, data.halo, haloRadius));
            propertyBlock.SetVector(GlowExitAxis, new Vector4(exitCentre.x, exitCentre.y, nozzleDown.x, nozzleDown.y));
            propertyBlock.SetVector(GlowExitShape, new Vector4(exitHalfWidth, exitLength, exitLight, exitRadius));
            propertyBlock.SetColor(GlowExitColor, data.exitColor);
            propertyBlock.SetColor(GlowColor, color);
            propertyBlock.SetFloat(GlowFalloff, data.falloff);
            propertyBlock.SetFloat(GlowOverlap, data.overlap);
            propertyBlock.SetFloat(GlowComposited, FlameGlowCompositor.Available ? 1 : 0);
            propertyBlock.SetVector(GlowNozzle, new Vector4(nozzleCentre.x, nozzleCentre.y, nozzleDown.x, nozzleDown.y));
            propertyBlock.SetVectorArray(GlowHighlightRect, highlightRect);
            propertyBlock.SetVectorArray(GlowHighlightUV, highlightUV);
            propertyBlock.SetVectorArray(GlowHighlightColor, highlightColor);
            if (highlightTexture != null)
                propertyBlock.SetTexture(HighlightTex, highlightTexture);
            glow.SetPropertyBlock(propertyBlock);

            // The quad is laid out by the shader, so the culling bounds have to be too
            glow.localBounds = new Bounds((min + max) * 0.5f, max - min);

            if (FlameGlowCompositor.Available)
                FlameGlowCompositor.Add(this);
        }

        // How much brighter the plume's light is at meshY with the eddies streaming down it, calm at the nozzle
        float GetEddies(float meshY, float stream)
        {
            float growth = Mathf.SmoothStep(0.3f, 1, meshY / EddyGrowth);
            float eddy = Mathf.Clamp(FlameMeshModule.GetNoise(eddySeed, (meshY - stream) * EddyTiles), -1, 2);
            return 1 + turbulence * growth * eddy;
        }

        // Where the plume's middle is at meshY, in the glow's space: its own jet's, going over to the group's as it merges
        Vector2 GetCentre(float meshY, Transform space)
        {
            flame.GetCrossSection(meshY, false, out Vector2 centre, out _, out _);

            float merge = flame.GetMergeAt(meshY);
            if (merge > 0)
            {
                flame.GetCrossSection(meshY, true, out Vector2 groupCentre, out _, out _);
                centre = Vector2.Lerp(centre, groupCentre, merge);
            }

            return space.InverseTransformPoint(centre);
        }

        // The plume across at meshY, in the glow's space: its half-width, and how bright it is over that.
        // Merged, this flame's share of the group's light, spread over the group's plume, so the group's glows add up to its plume's.
        void GetSlab(float meshY, Transform space, float toLocal, out float halfWidth, out float light)
        {
            flame.GetCrossSection(meshY, false, out _, out halfWidth, out _);
            light = flame.GetBrightness(meshY, false) * flame.exitPressure;

            float merge = flame.GetMergeAt(meshY);
            if (merge > 0)
            {
                flame.GetCrossSection(meshY, true, out _, out float groupHalfWidth, out _);
                float share = Mathf.Clamp01(flame.merge.share.y - flame.merge.share.x);
                float groupLight = flame.GetBrightness(meshY, true) * flame.merge.exitPressure * share;

                float mergedHalfWidth = Mathf.Lerp(halfWidth, groupHalfWidth, merge);
                light = Mathf.Lerp(light * halfWidth, groupLight * groupHalfWidth, merge) / Mathf.Max(mergedHalfWidth, 1e-6f);
                halfWidth = mergedHalfWidth;
            }

            halfWidth *= toLocal;
            light *= ThroatEmission;
        }

        #if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying)
                Apply(debugThrottle, debugVacuum);
        }
        #endif


        [Serializable]
        public struct GlowData
        {
            [Tooltip("Tints the colour the plume burns in")]
            [ColorUsage(false)] public Color color;
            [Tooltip("How bright the bloom tight around the plume is, as from the eye or a lens, for the plume's light")]
            [Min(0)] public float bloom;
            [Tooltip("How far it spreads, in nozzle half-widths")]
            [Min(0.01f)] public float bloomRadius;
            [Tooltip("How bright the wide halo the air scatters around the plume is")]
            [Min(0)] public float halo;
            [Tooltip("How far it spreads, in nozzle half-widths")]
            [Min(0.01f)] public float haloRadius;
            [Tooltip("How bright the glow of the nozzle exit is at its middle, from the bell and chamber seen through it")]
            [Min(0)] public float exit;
            [Tooltip("How far it spreads, in nozzle half-widths")]
            [Min(0.01f)] public float exitRadius;
            [ColorUsage(false)] public Color exitColor;
            [Tooltip("How bright the hot exhaust past the flame still glows, for the glow at the nozzle")]
            [Range(0, 1)] public float afterglow;
            [Tooltip("How much further than the flame it glows, of the flame's length")]
            [Min(0)] public float afterglowLength;
            [Tooltip("How far down the flame, of its length, the afterglow holds before fading out towards its end")]
            [Range(0, 1)] public float afterglowHold;
            [Tooltip("How quickly the plume's light saturates before it feeds the glow, as the flame's look does: 0 = not at all; the glow can't pass 1 / this")]
            [Min(0)] public float rolloff;
            [Tooltip("How quickly the glow fades away from the plume, against each spread's own radius: 0 = as the light would, below 0 it reaches further, above 0 it hugs the plume")]
            [Range(-1, 1)] public float falloff;
            [Tooltip("How little of its light adds up where it overlaps other engines' glows: 0 = all of it, 0.5 = two alike glows come to 1.4 of one, 1 = only the brightest shows")]
            [Range(0, 1)] public float overlap;
            [Tooltip("Sprites drawn at the nozzle exit that give it its depth, in nozzle half-widths across and up from its middle. Vacuum's are blended to by index.")]
            public Highlight[] highlights;

            public static GlowData Lerp(GlowData a, GlowData b, float t) => new GlowData
            {
                color = Color.Lerp(a.color, b.color, t),
                bloom = Mathf.Lerp(a.bloom, b.bloom, t),
                bloomRadius = Mathf.Lerp(a.bloomRadius, b.bloomRadius, t),
                halo = Mathf.Lerp(a.halo, b.halo, t),
                haloRadius = Mathf.Lerp(a.haloRadius, b.haloRadius, t),
                exit = Mathf.Lerp(a.exit, b.exit, t),
                exitRadius = Mathf.Lerp(a.exitRadius, b.exitRadius, t),
                exitColor = Color.Lerp(a.exitColor, b.exitColor, t),
                afterglowHold = Mathf.Lerp(a.afterglowHold, b.afterglowHold, t),
                afterglow = Mathf.Lerp(a.afterglow, b.afterglow, t),
                afterglowLength = Mathf.Lerp(a.afterglowLength, b.afterglowLength, t),
                rolloff = Mathf.Lerp(a.rolloff, b.rolloff, t),
                falloff = Mathf.Lerp(a.falloff, b.falloff, t),
                overlap = Mathf.Lerp(a.overlap, b.overlap, t),
            };
        }

        [Serializable]
        public struct Highlight
        {
            public Sprite sprite;
            public Color color;
            public Vector2 position;
            [Tooltip("As a sprite's scale; negative flips it")]
            public Vector2 scale;
        }
    }
}
