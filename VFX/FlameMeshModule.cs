using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public partial class FlameMeshModule : MonoBehaviour
    {
        static readonly int
            AdditiveBlend = Shader.PropertyToID("_AdditiveBlend"),
            Throttle = Shader.PropertyToID("_Throttle"),
            StripesWidth = Shader.PropertyToID("_StripesWidth"),
            StripesStrength = Shader.PropertyToID("_StripesStrength");

        static readonly int
            AirSpreadProperty = Shader.PropertyToID("airSpread"),
            SmokeCoverProperty = Shader.PropertyToID("smokeCover"),
            EdgeSoftnessProperty = Shader.PropertyToID("_EdgeSoftness"),
            EdgeBalanceProperty = Shader.PropertyToID("_EdgeBalance"),
            CoreLengthProperty = Shader.PropertyToID("_CoreLength"),
            MixedLightProperty = Shader.PropertyToID("_MixedLight"),
            EdgeLightProperty = Shader.PropertyToID("_EdgeLight"),
            NoiseSeed = Shader.PropertyToID("noiseSeed"),
            EddyOffset = Shader.PropertyToID("eddyOffset"),
            EddyFlicker = Shader.PropertyToID("eddyFlicker"),
            DiamondOffsetProperty = Shader.PropertyToID("diamondOffset"),
            FlameColorProperty = Shader.PropertyToID("_FlameColor"),
            FlameMaskProperty = Shader.PropertyToID("_FlameMask"),
            FlickerProperty = Shader.PropertyToID("flameFlicker"),
            ExitPressureProperty = Shader.PropertyToID("exitPressure"),
            AtmospherePressureProperty = Shader.PropertyToID("atmospherePressure"),
            AirTermsProperty = Shader.PropertyToID("airTerms"),
            WidthTermsProperty = Shader.PropertyToID("widthTerms"),
            IsMachDiamondsProperty = Shader.PropertyToID("isMachDiamondsShader");
        
        
        // Debug preview - refreshes on validate. EngineEffects drives this at runtime.
        #if UNITY_EDITOR
        [BoxGroup("Debug"), Range(0, 1)] public float debugThrottle, debugVacuum;
        #endif
        [BoxGroup("Debug"), ReadOnly, ShowInInspector] float atmospherePressureBar; // current ambient pressure (bar), updated each Apply
        
        const float Gamma = 1.2f; // ratio of specific heats for hot combustion products

        [Space]
        public float exitPressure = 0.5f; // nozzle-exit static pressure (bar)
        [Tooltip("How far the jet keeps spreading in air as it mixes with it, on down its tail too: nozzle half-widths per plume length")]
        [Min(0)] public float airSpread;
        [ReadOnly, ShowInInspector] float flameExitTemperature; // fraction of chamber temperature (1 = chamber)

        [Space]
        public float stripesWidth = 1;
        [Range(0, 3)] public float stripesStrength = 1;

        [Space]
        [Tooltip("How much the plume's brightness flickers")]
        [Range(0, 0.5f)] public float flicker = 0.05f;

        [NonSerialized] public float appliedThrottle;
        [NonSerialized] public float appliedAtmospherePressure;
        [NonSerialized] public SmokeTrailModule smokeTrail; // set by the smoke module while it's on, whose speed the turbulence streams at
        [NonSerialized] public Vector3 smokeCover; // meshY where the smoke over this flame starts to show, where it has, and how much it hides the flame there

        // The shader reads diamondOffset off the material, and the plume's shape on the CPU has to take it from the same place
        float diamondOffset = float.NaN;
        public float DiamondOffset
        {
            get
            {
                if (float.IsNaN(diamondOffset))
                {
                    Material material = GetPlumeMaterial(DiamondOffsetProperty);
                    diamondOffset = material != null ? material.GetFloat(DiamondOffsetProperty) : 0;
                }

                return diamondOffset;
            }
        }

        // Likewise the flame's colour, which its glow takes
        Color? flameColor;
        public Color FlameColor
        {
            get
            {
                if (flameColor == null)
                {
                    Material material = GetPlumeMaterial(FlameColorProperty);
                    flameColor = material != null ? material.GetColor(FlameColorProperty) : Color.white;
                }

                return flameColor.Value;
            }
        }

        // And how far in its outline softens, and how its light falls off past its potential core and out to its edges in air (GetMixedLight in the shader)
        float? edgeSoftness, edgeBalance, coreLength, mixedLight, edgeLight;
        public float EdgeSoftness => edgeSoftness ??= GetMaterialFloat(EdgeSoftnessProperty, 0);
        public float EdgeBalance => edgeBalance ??= GetMaterialFloat(EdgeBalanceProperty, 0.5f);
        public float CoreLength => coreLength ??= GetMaterialFloat(CoreLengthProperty, 0.35f);
        public float MixedLight => mixedLight ??= GetMaterialFloat(MixedLightProperty, 1);
        public float EdgeLight => edgeLight ??= GetMaterialFloat(EdgeLightProperty, 1);

        float GetMaterialFloat(int property, float fallback)
        {
            Material material = GetPlumeMaterial(property);
            return material != null ? material.GetFloat(property) : fallback;
        }

        // Where in the turbulence pattern this flame sits, so engines side by side don't churn in step
        [NonSerialized] public float noiseSeed;
        void Awake() => noiseSeed = UnityEngine.Random.value;

        // How the plume's brightness flickers now, about 1 at its spread; its glow flickers with it
        public float FlickerNoise { get; private set; }
        const float PlumeFlickerRate = 14, PlumeFlickerRateFine = 23; // noise tiles a second

        void UpdateFlicker()
        {
            float seed = noiseSeed * 100;
            float time = Time.time;
            FlickerNoise = Mathf.Clamp(0.6f * GetNoise(seed, time * PlumeFlickerRate) + 0.4f * GetNoise(seed + 31, time * PlumeFlickerRateFine), -1, 2);
        }

        // Mathf.PerlinNoise about its mean, at about 1 for its spread
        public static float GetNoise(float x, float y) => (Mathf.PerlinNoise(x, y) - 0.5f) / 0.2f;

        // The turbulence streams down the plume as fast as the smoke leaves it, in nozzle half-widths a second
        const float DefaultEddySpeed = 144; // for a flame that leaves no smoke to take it from
        public float EddySpeed => smokeTrail != null ? smokeTrail.jetSpeed : DefaultEddySpeed;

        // Noise tiles per plume length the turbulence's layers are laid down it at, eddyLarge and eddySmall in the shader
        const float EddyLarge = 0.6f, EddySmall = 1.2f;
        const float EddySmallSpeed = 1.25f; // the small eddies stream faster, so the pattern changes as it goes
        // Noise tiles per plume length streamed that the layers wander across it, opposite ways, so the pattern never comes round again
        const float LargeWander = 0.07f, SmallWander = -0.11f;
        const float FlickerRate = 0.9f, FlickerWander = 0.0731f; // noise tiles a second

        // How far the turbulence of a plume streaming at this many of its lengths a second has carried the noise by now, in tiles
        static Vector4 GetEddyOffset(float flow, double time)
        {
            double streamed = time * flow;
            return new Vector4(Frac(streamed * LargeWander), Frac(-streamed * EddyLarge), Frac(streamed * SmallWander), Frac(-streamed * EddySmall * EddySmallSpeed));
        }

        static Vector4 GetEddyFlicker(double time) => new(Frac(time * FlickerWander), Frac(-time * FlickerRate));
        static float Frac(double value) => (float)(value - Math.Floor(value));

        // Plume lengths a second its turbulence streams down the plume drawn in that space
        float GetEddyFlow(Transform space) => EddySpeed * space.TransformVector(Vector3.right).magnitude / (GetPlumeLength(exitPressure) * Mathf.Max(space.TransformVector(Vector3.up).magnitude, 1e-6f));
        public float GetEddyFlow() => GetEddyFlow(GetPlumeSpace());

        // The plume's own material (not the shock train's), provided it has the given property
        Material GetPlumeMaterial(int property)
        {
            foreach (MeshRef a in meshRenderers)
            {
                Material material = a.machDiamondsMesh || a.meshRenderer == null ? null : a.meshRenderer.sharedMaterial;
                if (material != null && material.HasProperty(property))
                    return material;
            }

            return null;
        }

        void OnValidate()
        {
            flameExitTemperature = Mathf.Pow(exitPressure, (Gamma - 1f) / Gamma); // isentropic T/Tc = (P/Pc)^((g-1)/g)

            #if UNITY_EDITOR
            atmospherePressureBar = DebugAtmospherePressure(debugVacuum);
            if (!Application.isPlaying && meshRenderers != null)
                ApplyDebug(debugThrottle, debugVacuum);
            #endif
        }

        void OnDisable() => FlameCompositor.Remove(this);


        // Ref
        [Space]
        [Space]
        public MeshRef[] meshRenderers;

        [Space]
        public Data groundData;
        [Space]
        public Data vacuumData;


        // Editor-preview path - no world to read, so derive the ambient pressure from the vacuum slider.
        public void ApplyDebug(float throttle, float vacuum)
            => Apply(throttle, vacuum, DebugAtmospherePressure(vacuum));
        
        public void Apply(float throttle, float vacuum, float atmospherePressure)
        {
            bool on = throttle > 0;
            float additiveBlend = Mathf.Lerp(groundData.additiveBlend, vacuumData.additiveBlend, vacuum);

            appliedThrottle = throttle;
            appliedAtmospherePressure = atmospherePressure;
            
            UpdateFlicker();
            if (on && FlameCompositor.Available)
                FlameCompositor.Add(this);

            PlumeState plume = default;
            FillPlumeParams(ref plume);
            Vector4 airTerms = new(PlumeMath.GetTailPresence(atmospherePressure), PlumeMath.GetTailChurn(atmospherePressure), PlumeMath.GetTailStart(atmospherePressure), Mathf.Sqrt(Mathf.Clamp01(atmospherePressure)));
            Vector4 widthTerms = PlumeMath.GetWidthTerms(exitPressure, throttle, atmospherePressure, plume.diamondOffset);

            // Set
            foreach (MeshRef a in meshRenderers)
            {
                // Disable the mesh entirely when the engine is off
                GameObject go = a.meshRenderer.gameObject;
                if (go.activeSelf != on)
                    go.SetActive(on);

                if (!on)
                    continue;

                MaterialPropertyBlock propertyBlock = a.propertyBlock ??= new MaterialPropertyBlock();
                propertyBlock.Clear();

                propertyBlock.SetFloat(Throttle, throttle); // * Mathf.Lerp(1, 0.75f, vacuum));

                propertyBlock.SetFloat(AdditiveBlend, additiveBlend);
                
                propertyBlock.SetFloat(ExitPressureProperty, exitPressure); // nozzle-exit static pressure (bar)
                propertyBlock.SetFloat(AirSpreadProperty, airSpread);
                propertyBlock.SetVector(SmokeCoverProperty, smokeCover);

                propertyBlock.SetFloat(AtmospherePressureProperty, atmospherePressure); // 1 bar at ground -> 0 in vacuum, falling off exponentially with height
                propertyBlock.SetVector(AirTermsProperty, airTerms);
                propertyBlock.SetVector(WidthTermsProperty, widthTerms);

                propertyBlock.SetFloat(IsMachDiamondsProperty, a.machDiamondsMesh? 1 : 0);
                
                propertyBlock.SetFloat(StripesWidth, stripesWidth);
                propertyBlock.SetFloat(StripesStrength, stripesStrength);

                propertyBlock.SetFloat(NoiseSeed, noiseSeed);
                propertyBlock.SetFloat(FlickerProperty, flicker * FlickerNoise);

                propertyBlock.SetVector(EddyOffset, GetEddyOffset(GetEddyFlow(a.meshRenderer.transform), Time.timeAsDouble));
                propertyBlock.SetVector(EddyFlicker, GetEddyFlicker(Time.timeAsDouble));

                a.meshRenderer.SetPropertyBlock(propertyBlock, 0);
                
                // Fix for frustum culling
                // Note: This has to be updated along with the flame shader
                a.meshRenderer.localBounds = ComputeFlameBounds(a, ref plume);
            }
        }
        
        // meshY it has faded out at, tail included
        public float GetFadedOutMeshY()
        {
            PlumeState plume = default;
            FillPlumeParams(ref plume);
            return PlumeMath.GetFadedOutMeshY(plume);
        }

        // The plume at meshY in world space, as its middle, half-width, and how far down it that is.
        // For more than a sample or two, take GetPlume once and sample it with PlumeMath.
        public void GetCrossSection(float meshY, out Vector2 centre, out float halfWidth, out float distance)
        {
            GetPlume(out PlumeState plume);
            PlumeMath.GetCrossSection(plume, meshY, out centre, out halfWidth, out distance);
        }

        // The meshY that lies the given distance down the plume, as GetCrossSection measures it
        public float GetMeshY(float distance)
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetMeshY(plume, distance);
        }

        // How bright the plume is down its middle at meshY, relative to a nozzle, as the shader has it
        public float GetBrightness(float meshY)
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetBrightness(ref plume, meshY);
        }

        // The half-width of the nozzle the plume comes out of, in world space
        public float GetNozzleRadius()
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetNozzleRadius(plume);
        }

        // Everything the plume is sampled from, as it is now, for PlumeMath
        public unsafe void GetPlume(out PlumeState plume)
        {
            plume = default;
            FillPlumeParams(ref plume);
            plume.toWorld = GetPlumeSpace().localToWorldMatrix;
            plume.scaleX = ((Vector3)plume.toWorld.GetColumn(0)).magnitude;
            plume.scaleY = ((Vector3)plume.toWorld.GetColumn(1)).magnitude;

            float[] profile = MaskProfile;
            plume.maskLength = Mathf.Min(profile.Length, PlumeMath.MaskSamples);
            for (int i = 0; i < plume.maskLength; i++)
                plume.mask[i] = profile[i];
        }

        // The parts of it that don't depend on where it is
        void FillPlumeParams(ref PlumeState plume)
        {
            plume.exitPressure = exitPressure;
            plume.airSpread = airSpread;
            plume.throttle = appliedThrottle;
            plume.atmospherePressure = appliedAtmospherePressure;
            plume.diamondOffset = DiamondOffset;
            plume.coreLength = CoreLength;
            plume.mixedLight = MixedLight;
            plume.edgeLight = EdgeLight;
            plume.edgeSoftness = EdgeSoftness;
            plume.edgeBalance = EdgeBalance;
        }

        // The renderer the plume is drawn with
        Transform GetPlumeSpace()
        {
            foreach (MeshRef a in meshRenderers)
                if (!a.machDiamondsMesh && a.meshRenderer != null)
                    return a.meshRenderer.transform;

            return transform;
        }

        // The flame mask's alpha down the plume's middle, which the shader samples at 1 - v. It can't be read on the CPU, so it's read back once per texture.
        static readonly Dictionary<Texture, float[]> maskProfiles = new();
        [NonSerialized] float[] maskProfile;

        float[] MaskProfile
        {
            get
            {
                if (maskProfile == null)
                {
                    Material material = GetPlumeMaterial(FlameMaskProperty);
                    maskProfile = GetMaskProfile(material != null ? material.GetTexture(FlameMaskProperty) : null);
                }

                return maskProfile;
            }
        }

        static float[] GetMaskProfile(Texture mask)
        {
            if (mask == null)
                return new float[] { 1, 1 };
            if (maskProfiles.TryGetValue(mask, out float[] profile))
                return profile;

            const int Samples = 32;
            RenderTexture target = RenderTexture.GetTemporary(1, Samples, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture active = RenderTexture.active;
            Graphics.Blit(mask, target);

            RenderTexture.active = target;
            Texture2D readback = new(1, Samples, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, 1, Samples), 0, 0);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(target);

            profile = new float[Samples];
            for (int i = 0; i < Samples; i++)
                profile[i] = readback.GetPixel(0, i).a;
            if (Application.isPlaying)
                Destroy(readback);
            else
                DestroyImmediate(readback);

            maskProfiles[mask] = profile;
            return profile;
        }

        internal static Mesh GetMesh(MeshRef meshRef)
        {
            if (meshRef.meshFilter == null)
                meshRef.meshFilter = meshRef.meshRenderer.GetComponent<MeshFilter>();

            return meshRef.meshFilter != null ? meshRef.meshFilter.sharedMesh : null;
        }

        Bounds ComputeFlameBounds(MeshRef meshRef, ref PlumeState plume)
        {
            Mesh mesh = GetMesh(meshRef);
            if (mesh == null)
                return meshRef.meshRenderer.localBounds; // nothing to base it on; leave as-is

            Bounds original = mesh.bounds;

            float maxMeshY = Mathf.Max(Mathf.Abs(original.min.y), Mathf.Abs(original.max.y));
            float maxMeshZ = Mathf.Max(Mathf.Abs(original.min.z), Mathf.Abs(original.max.z));

            PlumeMath.GetFlameBounds(ref plume, meshRef.machDiamondsMesh, maxMeshY, maxMeshZ, out Vector3 min, out Vector3 max);

            Bounds result = new Bounds();
            result.SetMinMax(min, max);
            return result;
        }

        // The plume's drawn length in local units (matches the y scale in VertCore).
        public static float GetPlumeLength(float exitPressure) => PlumeMath.GetPlumeLength(exitPressure);

        // Mirrors the shader; without the ripple it is the smooth envelope. y is how far down the plume, of its length; past 1 only the spread in air goes on.
        public static float GetFlameWidth(float y, float exitPressure, float throttle, float spread, float atmospherePressure, float diamondOffset, bool ripple = true)
            => PlumeMath.GetFlameWidth(y, exitPressure, throttle, spread, atmospherePressure, diamondOffset, ripple);

        // Real atmospheres thin out exponentially with altitude (barometric law: P ~ exp(-h/H)).
        // The vacuum value (0 at ground -> 1 in space) stands in for height, so map it through that
        // curve, normalised so vacuum 0 gives exactly 1 bar and vacuum 1 gives exactly 0.
        // k matches Earth: its atmosphere curve is 10, and the vacuum slider spans 0.05..0.8 of the
        // atmosphere height (a 0.75 range), so the curve seen across the slider is 10 * 0.75 = 7.5.
        static float DebugAtmospherePressure(float vacuum)
        {
            const float K = 7.5f;
            float floor = Mathf.Exp(-K); // pressure the raw curve would give at vacuum = 1
            return (Mathf.Exp(-K * vacuum) - floor) / (1f - floor);
        }

        [Serializable]
        public class Data
        {
            [Range(0, 1)] public float additiveBlend;
        }
        
        [Serializable]
        public class MeshRef
        {
            public MeshRenderer meshRenderer;
            public bool machDiamondsMesh;

            [NonSerialized] public MeshFilter meshFilter; // cached lazily for the culling-bounds override
            [NonSerialized] public MaterialPropertyBlock propertyBlock;
        }
    }
}