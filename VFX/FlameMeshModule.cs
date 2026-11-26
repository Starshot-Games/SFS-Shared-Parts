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
        
        // Plume merging - see FlameMergeSolver
        static readonly int
            MergeAmount = Shader.PropertyToID("mergeAmount"),
            MergeContact = Shader.PropertyToID("mergeContact"),
            MergeFadeLength = Shader.PropertyToID("mergeFadeLength"),
            MergeShare = Shader.PropertyToID("mergeShare"),
            MergeLitShare = Shader.PropertyToID("mergeLitShare"),
            MergeJoinWeight = Shader.PropertyToID("mergeJoinWeight"),
            MergeShareFadeLength = Shader.PropertyToID("mergeShareFadeLength"),
            MergeNozzleBounds = Shader.PropertyToID("mergeNozzleBounds"),
            MergeDrift = Shader.PropertyToID("mergeDrift"),
            MergeBend = Shader.PropertyToID("mergeBend"),
            MergeCloseLength = Shader.PropertyToID("mergeCloseLength"),
            MergeStripeScale = Shader.PropertyToID("mergeStripeScale"),
            MergeRowStep = Shader.PropertyToID("mergeRowStep"),
            MergeLead = Shader.PropertyToID("mergeLead"),
            MergeRefLength = Shader.PropertyToID("mergeRefLength"),
            NeighbourFrame = Shader.PropertyToID("neighbourFrame"),
            NeighbourFlow = Shader.PropertyToID("neighbourFlow"),
            NeighbourBend = Shader.PropertyToID("neighbourBend"),
            NeighbourEddyOffset = Shader.PropertyToID("neighbourEddyOffset"),
            MergeCenter = Shader.PropertyToID("mergeCenter"),
            MergeHalfSpan = Shader.PropertyToID("mergeHalfSpan"),
            MergeThroat = Shader.PropertyToID("mergeThroat"),
            MergeGroupLength = Shader.PropertyToID("mergeGroupLength"),
            MergeAlongOffset = Shader.PropertyToID("mergeAlongOffset"),
            MergeExitPressure = Shader.PropertyToID("mergeExitPressure"),
            MergeThrottle = Shader.PropertyToID("mergeThrottle"),
            MergeAirSpread = Shader.PropertyToID("mergeAirSpread"),
            NeighbourSpread = Shader.PropertyToID("neighbourSpread"),
            AirSpreadProperty = Shader.PropertyToID("airSpread"),
            SmokeCoverProperty = Shader.PropertyToID("smokeCover"),
            MergeColor = Shader.PropertyToID("mergeColor"),
            MergeCoreColor = Shader.PropertyToID("mergeCoreColor"),
            MergeTailColor = Shader.PropertyToID("mergeTailColor"),
            MergeOuterColor = Shader.PropertyToID("mergeOuterColor"),
            MergeOuterWidth = Shader.PropertyToID("mergeOuterWidth"),
            MergeEdgeSoftness = Shader.PropertyToID("mergeEdgeSoftness"),
            MergeEdgeBalance = Shader.PropertyToID("mergeEdgeBalance"),
            OuterColorProperty = Shader.PropertyToID("_OuterColor"),
            OuterWidthProperty = Shader.PropertyToID("_OuterWidth"),
            EdgeSoftnessProperty = Shader.PropertyToID("_EdgeSoftness"),
            EdgeBalanceProperty = Shader.PropertyToID("_EdgeBalance"),
            MergeAdditiveBlend = Shader.PropertyToID("mergeAdditiveBlend"),
            MergeStripesWidth = Shader.PropertyToID("mergeStripesWidth"),
            MergeStripesStrength = Shader.PropertyToID("mergeStripesStrength"),
            MergeSoot = Shader.PropertyToID("mergeSoot"),
            ExposureProperty = Shader.PropertyToID("_Exposure"),
            CoreLengthProperty = Shader.PropertyToID("_CoreLength"),
            MixedLightProperty = Shader.PropertyToID("_MixedLight"),
            EdgeLightProperty = Shader.PropertyToID("_EdgeLight"),
            OpacityProperty = Shader.PropertyToID("_Opacity"),
            OpaqueExposureProperty = Shader.PropertyToID("_OpaqueExposure"),
            MergeOpaqueExposure = Shader.PropertyToID("mergeOpaqueExposure"),
            SkyHoldProperty = Shader.PropertyToID("_SkyHold"),
            MergeSkyHold = Shader.PropertyToID("mergeSkyHold"),
            MergeOpacity = Shader.PropertyToID("mergeOpacity"),
            MergeExposure = Shader.PropertyToID("mergeExposure"),
            MergeCoreLength = Shader.PropertyToID("mergeCoreLength"),
            CoreWidthProperty = Shader.PropertyToID("_CoreWidth"),
            MergeCoreWidth = Shader.PropertyToID("mergeCoreWidth"),
            ColorSoftnessProperty = Shader.PropertyToID("_ColorSoftness"),
            MergeColorSoftness = Shader.PropertyToID("mergeColorSoftness"),
            TailColorShiftProperty = Shader.PropertyToID("_TailColorShift"),
            MergeTailColorShift = Shader.PropertyToID("mergeTailColorShift"),
            MergeMixedLight = Shader.PropertyToID("mergeMixedLight"),
            MergeEdgeLight = Shader.PropertyToID("mergeEdgeLight"),
            NeighbourSoot = Shader.PropertyToID("neighbourSoot"),
            MergeTurbulence = Shader.PropertyToID("mergeTurbulence"),
            MergeMirror = Shader.PropertyToID("mergeMirror"),
            MergeSeed = Shader.PropertyToID("mergeSeed"),
            NoiseSeed = Shader.PropertyToID("noiseSeed"),
            EddyOffset = Shader.PropertyToID("eddyOffset"),
            MergeEddyOffset = Shader.PropertyToID("mergeEddyOffset"),
            EddyFlicker = Shader.PropertyToID("eddyFlicker"),
            TurbulenceProperty = Shader.PropertyToID("_Turbulence"),
            RoughnessProperty = Shader.PropertyToID("_Roughness"),
            SootProperty = Shader.PropertyToID("_Soot"),
            GlareAxis = Shader.PropertyToID("glareAxis"),
            GlareShape = Shader.PropertyToID("glareShape"),
            GlareFlow = Shader.PropertyToID("glareFlow"),
            DiamondOffsetProperty = Shader.PropertyToID("diamondOffset"),
            FlameColorProperty = Shader.PropertyToID("_FlameColor"),
            TailColorProperty = Shader.PropertyToID("_TailColor"),
            CoreColorProperty = Shader.PropertyToID("_CoreColor"),
            CoreBlendProperty = Shader.PropertyToID("_CoreBlend"),
            FlameMaskProperty = Shader.PropertyToID("_FlameMask"),
            FlickerProperty = Shader.PropertyToID("flameFlicker"),
            ExitPressureProperty = Shader.PropertyToID("exitPressure"),
            AtmospherePressureProperty = Shader.PropertyToID("atmospherePressure"),
            AirTermsProperty = Shader.PropertyToID("airTerms"),
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
        [Tooltip("Whether this flame streaks the material's soot near the nozzle")]
        public bool sootStreaks = true;

        // How much light this flame gives off next to other engines'.
        [Space]
        [Range(0, 1)] public float mergeLuminosity = 1;

        [Tooltip("How much the plume's brightness flickers")]
        [Range(0, 0.5f)] public float flicker = 0.05f;

        // Plume merging. The solver reads these off every live flame and writes back the slice of
        // the shared plume this one draws, and the far brighter plumes around that drown it out
        // wherever it lies under them.
        [NonSerialized] public float appliedThrottle;
        [NonSerialized] public float appliedAtmospherePressure;
        [NonSerialized] public float appliedAdditiveBlend;
        [NonSerialized] public FlameMerge merge;
        [NonSerialized] public SmokeTrailModule smokeTrail; // set by the smoke module while it's on, so a merged group can pick one to send out its smoke
        [NonSerialized] public Vector3 smokeCover; // meshY where the smoke over this flame starts to show, where it has, and how much it hides the flame there
        [NonSerialized] public FlameGlare[] glare = new FlameGlare[FlameMergeSolver.MaxGlareSources];
        [NonSerialized] public int glareCount;
        [NonSerialized] public FlameMergeSolver.Inputs mergeInputs;
        [NonSerialized] internal Transform solveRootTransform; // the craft it was last solved on, kept so sorting by it needn't ask the transform each time
        [NonSerialized] internal int solveRoot;
        bool registered;

        // Scratch for handing the glare to the shader, which takes it as arrays. Always sent whole:
        // a property block fixes an array's length the first time it is set.
        static readonly Vector4[] glareAxis = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] glareShape = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] glareFlow = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] neighbourFrame = new Vector4[2];
        static readonly Vector4[] neighbourFlow = new Vector4[2];
        static readonly Vector4[] neighbourBend = new Vector4[2];
        static readonly Vector4[] neighbourEddyOffset = new Vector4[2];

        // The shader reads diamondOffset off the material, and it shifts the envelope enough to move
        // where two plumes touch - so the solver has to take it from the same place.
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

        // Likewise the flame's colour, which the solver mixes into the merged plume's.
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

        // And down its tail
        Color? tailColor;
        public Color TailColor
        {
            get
            {
                if (tailColor == null)
                {
                    Material material = GetPlumeMaterial(TailColorProperty);
                    tailColor = material != null ? material.GetColor(TailColorProperty) : FlameColor;
                }

                return tailColor.Value;
            }
        }
        // And out at its edges, how far in that reaches, and how far in its outline softens
        Color? outerColor;
        public Color OuterColor
        {
            get
            {
                if (outerColor == null)
                {
                    Material material = GetPlumeMaterial(OuterColorProperty);
                    outerColor = material != null ? material.GetColor(OuterColorProperty) : FlameColor;
                }

                return outerColor.Value;
            }
        }
        float? outerWidth, edgeSoftness, edgeBalance;
        public float OuterWidth => outerWidth ??= GetMaterialFloat(OuterWidthProperty, 0.4f);
        public float EdgeSoftness => edgeSoftness ??= GetMaterialFloat(EdgeSoftnessProperty, 0);
        public float EdgeBalance => edgeBalance ??= GetMaterialFloat(EdgeBalanceProperty, 0.5f);

        float? tailColorShift;
        public float TailColorShift => tailColorShift ??= GetMaterialFloat(TailColorShiftProperty, 0);

        // And the colour down its core, and how far it takes it there
        Color? coreColor;
        public Color CoreColor
        {
            get
            {
                if (coreColor == null)
                {
                    Material material = GetPlumeMaterial(CoreColorProperty);
                    coreColor = material != null ? material.GetColor(CoreColorProperty) : Color.white;
                }

                return coreColor.Value;
            }
        }
        float? coreBlend;
        public float CoreBlend => coreBlend ??= GetMaterialFloat(CoreBlendProperty, 0);
        float? coreWidth;
        public float CoreWidth => coreWidth ??= GetMaterialFloat(CoreWidthProperty, 0.7f);
        float? colorSoftness;
        public float ColorSoftness => colorSoftness ??= GetMaterialFloat(ColorSoftnessProperty, 0.5f);

        // And how turbulent it gets, in air and in vacuum
        Vector2? turbulence;
        public Vector2 Turbulence
        {
            get
            {
                if (turbulence == null)
                {
                    Material material = GetPlumeMaterial(TurbulenceProperty);
                    turbulence = material != null ? new Vector2(material.GetFloat(TurbulenceProperty), material.GetFloat(RoughnessProperty)) : Vector2.zero;
                }

                return turbulence.Value;
            }
        }

        // And how dark the soot it streaks near the nozzle is
        float? soot;
        public float Soot
        {
            get
            {
                if (soot == null)
                {
                    Material material = GetPlumeMaterial(SootProperty);
                    soot = material != null ? material.GetFloat(SootProperty) : 0;
                }

                return sootStreaks ? soot.Value : 0;
            }
        }

        // And how brightly it is seen in air, and how its light falls off past its potential core and out to its edges there (GetMixedLight in the shader)
        float? exposure, coreLength, mixedLight, edgeLight, opacity, opaqueExposure, skyHold;
        public float Exposure => exposure ??= GetMaterialFloat(ExposureProperty, 1);
        public float CoreLength => coreLength ??= GetMaterialFloat(CoreLengthProperty, 0.35f);
        public float MixedLight => mixedLight ??= GetMaterialFloat(MixedLightProperty, 1);
        public float EdgeLight => edgeLight ??= GetMaterialFloat(EdgeLightProperty, 1);
        public float Opacity => opacity ??= GetMaterialFloat(OpacityProperty, 1);
        public float OpaqueExposure => opaqueExposure ??= GetMaterialFloat(OpaqueExposureProperty, 1);
        public float SkyHold => skyHold ??= GetMaterialFloat(SkyHoldProperty, 0);

        float GetMaterialFloat(int property, float fallback)
        {
            Material material = GetPlumeMaterial(property);
            return material != null ? material.GetFloat(property) : fallback;
        }

        // Where in the turbulence pattern this flame sits, so engines side by side don't churn in step
        [NonSerialized] public float noiseSeed;
        void Awake() => noiseSeed = UnityEngine.Random.value;

        // How the plume's brightness flickers now, about 1 at its spread; its glow flickers with it.
        // A merged group flickers as one, or its slices would part at the joins.
        public float FlickerNoise { get; private set; }
        const float PlumeFlickerRate = 14, PlumeFlickerRateFine = 23; // noise tiles a second

        void UpdateFlicker()
        {
            float seed = (merge.amount > 0 ? merge.seed : noiseSeed) * 100;
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
        public float GetEddyFlow() => GetEddyFlow(GetPlumeSpace(out _));

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

        // Only live flames merge - the editor preview has no craft around it.
        void OnEnable()
        {
            if (!Application.isPlaying || registered)
                return;

            FlameMergeSolver.Register(this);
            registered = true;
        }
        void OnDisable()
        {
            FlameCompositor.Remove(this);
            if (!registered)
                return;

            FlameMergeSolver.Unregister(this);
            registered = false;
            merge = default;
            glareCount = 0;
        }


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
            appliedAdditiveBlend = additiveBlend;
            
            if (!registered && Application.isPlaying && isActiveAndEnabled)
            {
                FlameMergeSolver.Register(this);
                registered = true;
            }
            if (registered)
                FlameMergeSolver.EnsureSolved();
            UpdateFlicker();
            if (on && FlameCompositor.Available)
                FlameCompositor.Add(this);

            PlumeState plume = default;
            FillPlumeParams(ref plume);
            Vector4 airTerms = new(PlumeMath.GetTailPresence(atmospherePressure), PlumeMath.GetTailChurn(atmospherePressure), PlumeMath.GetTailStart(atmospherePressure), Mathf.Sqrt(Mathf.Clamp01(atmospherePressure)));

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

                // The merge is solved in module space; the shader runs in the renderer's, and a mesh
                // child can sit rotated or scaled relative to the module.
                Matrix4x4 toModule = transform.worldToLocalMatrix * a.meshRenderer.transform.localToWorldMatrix;
                float acrossScale = toModule.MultiplyVector(Vector3.right).x;
                float alongScale = toModule.MultiplyVector(Vector3.up).y;
                FlameMerge meshMerge = merge.InSpaceOf(acrossScale, alongScale);

                propertyBlock.SetFloat(Throttle, throttle); // * Mathf.Lerp(1, 0.75f, vacuum));

                propertyBlock.SetFloat(AdditiveBlend, additiveBlend);
                
                propertyBlock.SetFloat(ExitPressureProperty, exitPressure); // nozzle-exit static pressure (bar)
                propertyBlock.SetFloat(AirSpreadProperty, airSpread);
                propertyBlock.SetVector(SmokeCoverProperty, smokeCover);

                propertyBlock.SetFloat(AtmospherePressureProperty, atmospherePressure); // 1 bar at ground -> 0 in vacuum, falling off exponentially with height
                propertyBlock.SetVector(AirTermsProperty, airTerms);

                propertyBlock.SetFloat(IsMachDiamondsProperty, a.machDiamondsMesh? 1 : 0);
                
                propertyBlock.SetFloat(StripesWidth, stripesWidth);
                propertyBlock.SetFloat(StripesStrength, stripesStrength);
                if (!sootStreaks)
                    propertyBlock.SetFloat(SootProperty, 0);

                // The far brighter plumes this flame is drowned out under (a depth of 0 = none there)
                for (int i = 0; i < glareAxis.Length; i++)
                {
                    FlameGlare g = i < glareCount ? glare[i].InSpaceOf(acrossScale, alongScale) : default;

                    glareAxis[i] = new Vector4(g.origin.x, g.origin.y, g.slope, g.depth);
                    glareShape[i] = new Vector4(g.halfSpan, g.throat, g.length);
                    glareFlow[i] = new Vector4(g.exitPressure, g.throttle, g.airSpread);
                }
                propertyBlock.SetVectorArray(GlareAxis, glareAxis);
                propertyBlock.SetVectorArray(GlareShape, glareShape);
                propertyBlock.SetVectorArray(GlareFlow, glareFlow);

                // Merged plume (mergeAmount 0 = not merging, and the shader draws it as it always did)
                propertyBlock.SetFloat(MergeAmount, meshMerge.amount);
                propertyBlock.SetFloat(MergeContact, meshMerge.contact);
                propertyBlock.SetFloat(MergeFadeLength, meshMerge.fadeLength);
                propertyBlock.SetVector(MergeShare, meshMerge.share);
                propertyBlock.SetVector(MergeLitShare, meshMerge.litShare);
                propertyBlock.SetVector(MergeJoinWeight, meshMerge.joinWeight);
                propertyBlock.SetFloat(MergeShareFadeLength, meshMerge.shareFadeLength);
                propertyBlock.SetVector(MergeNozzleBounds, meshMerge.nozzleBounds);
                propertyBlock.SetFloat(MergeDrift, meshMerge.drift);
                propertyBlock.SetFloat(MergeBend, meshMerge.bend);
                propertyBlock.SetVector(MergeCloseLength, meshMerge.closeLength);
                propertyBlock.SetFloat(MergeStripeScale, meshMerge.stripeScale);
                propertyBlock.SetFloat(MergeRowStep, GetRowStep(GetMesh(a)));
                propertyBlock.SetFloat(MergeLead, meshMerge.lead);
                propertyBlock.SetFloat(MergeRefLength, meshMerge.refLength);
                SetNeighbour(0, meshMerge.left, throttle);
                SetNeighbour(1, meshMerge.right, throttle);
                propertyBlock.SetVectorArray(NeighbourFrame, neighbourFrame);
                propertyBlock.SetVectorArray(NeighbourFlow, neighbourFlow);
                propertyBlock.SetVectorArray(NeighbourBend, neighbourBend);
                propertyBlock.SetVectorArray(NeighbourEddyOffset, neighbourEddyOffset);
                propertyBlock.SetVector(NeighbourSpread, new Vector4(meshMerge.left.present ? meshMerge.left.airSpread : 0, meshMerge.right.present ? meshMerge.right.airSpread : 0));
                propertyBlock.SetVector(NeighbourSoot, new Vector4(meshMerge.left.present ? meshMerge.left.soot : 0, meshMerge.right.present ? meshMerge.right.soot : 0));
                propertyBlock.SetFloat(MergeCenter, meshMerge.center);
                propertyBlock.SetFloat(MergeHalfSpan, meshMerge.halfSpan);
                propertyBlock.SetFloat(MergeThroat, meshMerge.throat);
                propertyBlock.SetFloat(MergeGroupLength, meshMerge.groupLength);
                propertyBlock.SetFloat(MergeAlongOffset, meshMerge.alongOffset);
                propertyBlock.SetFloat(MergeExitPressure, meshMerge.exitPressure);
                propertyBlock.SetFloat(MergeThrottle, meshMerge.throttle);
                propertyBlock.SetFloat(MergeAirSpread, meshMerge.airSpread);
                propertyBlock.SetColor(MergeColor, meshMerge.color);
                propertyBlock.SetColor(MergeCoreColor, meshMerge.coreColor);
                propertyBlock.SetColor(MergeTailColor, meshMerge.tailColor);
                propertyBlock.SetFloat(MergeTailColorShift, meshMerge.tailColorShift);
                propertyBlock.SetColor(MergeOuterColor, meshMerge.outerColor);
                propertyBlock.SetFloat(MergeOuterWidth, meshMerge.outerWidth);
                propertyBlock.SetFloat(MergeEdgeSoftness, meshMerge.edgeSoftness);
                propertyBlock.SetFloat(MergeEdgeBalance, meshMerge.edgeBalance);
                propertyBlock.SetFloat(MergeAdditiveBlend, meshMerge.additiveBlend);
                propertyBlock.SetFloat(MergeStripesWidth, meshMerge.stripesWidth);
                propertyBlock.SetFloat(MergeStripesStrength, meshMerge.stripesStrength);
                propertyBlock.SetFloat(MergeSoot, meshMerge.soot);
                propertyBlock.SetFloat(MergeExposure, meshMerge.exposure);
                propertyBlock.SetFloat(MergeCoreLength, meshMerge.coreLength);
                propertyBlock.SetFloat(MergeCoreWidth, meshMerge.coreWidth);
                propertyBlock.SetFloat(MergeColorSoftness, meshMerge.colorSoftness);
                propertyBlock.SetFloat(MergeMixedLight, meshMerge.mixedLight);
                propertyBlock.SetFloat(MergeEdgeLight, meshMerge.edgeLight);
                propertyBlock.SetFloat(MergeOpacity, meshMerge.opacity);
                propertyBlock.SetFloat(MergeOpaqueExposure, meshMerge.opaqueExposure);
                propertyBlock.SetFloat(MergeSkyHold, meshMerge.skyHold);
                propertyBlock.SetVector(MergeTurbulence, meshMerge.turbulence);
                propertyBlock.SetFloat(MergeMirror, meshMerge.mirror);
                propertyBlock.SetFloat(MergeSeed, meshMerge.seed);
                propertyBlock.SetFloat(NoiseSeed, noiseSeed);
                propertyBlock.SetFloat(FlickerProperty, flicker * FlickerNoise);

                propertyBlock.SetVector(EddyOffset, GetEddyOffset(GetEddyFlow(a.meshRenderer.transform), Time.timeAsDouble));
                propertyBlock.SetVector(MergeEddyOffset, GetEddyOffset(meshMerge.eddyFlow, Time.timeAsDouble));
                propertyBlock.SetVector(EddyFlicker, GetEddyFlicker(Time.timeAsDouble));

                a.meshRenderer.SetPropertyBlock(propertyBlock, 0);
                
                // Fix for frustum culling
                // Note: This has to be updated along with the flame shader
                a.meshRenderer.localBounds = ComputeFlameBounds(a, ref plume, ref meshMerge);
            }
        }
        
        // A missing neighbour still goes in as a valid jet, so the shader never divides by zero working it out
        void SetNeighbour(int i, FlameNeighbour n, float throttle)
        {
            neighbourFrame[i] = n.present ? new Vector4(n.origin.x, n.origin.y, n.scale.x, n.scale.y) : new Vector4(0, 0, 1, 1);
            neighbourFlow[i] = n.present ? new Vector4(n.slope, n.exitPressure, n.throttle, 1) : new Vector4(0, exitPressure, throttle, 0);
            neighbourBend[i] = n.present ? new Vector4(n.drift, n.bend, n.seed, n.amount) : Vector4.zero;
            neighbourEddyOffset[i] = n.present ? GetEddyOffset(n.eddyFlow, Time.timeAsDouble) : Vector4.zero;
        }

        public float GetGlowVisibility()
        {
            if (glareCount == 0)
                return 1;

            const int samples = 4;
            const float reach = 0.15f; // of the plume's own length

            float visibility = 0;
            for (int s = 0; s < samples; s++)
            {
                float along = GetPlumeLength(exitPressure) * reach * s / (samples - 1);

                float drowned = 0;
                for (int i = 0; i < glareCount; i++)
                    drowned = Mathf.Max(drowned, glare[i].GetDrowned(0, along, appliedAtmospherePressure, DiamondOffset));

                visibility += 1 - drowned;
            }

            return visibility / samples;
        }

        // meshY it has faded out at, tail included
        public float GetFadedOutMeshY(bool group)
        {
            PlumeState plume = default;
            FillPlumeParams(ref plume);
            plume.merge = merge;
            return PlumeMath.GetFadedOutMeshY(plume, group);
        }

        // The plume at meshY in world space - this jet, or the group's merged plume - as its middle, half-width, and how far down it that is.
        // For more than a sample or two, take GetPlume once and sample it with PlumeMath.
        public void GetCrossSection(float meshY, bool group, out Vector2 centre, out float halfWidth, out float distance)
        {
            GetPlume(out PlumeState plume);
            PlumeMath.GetCrossSection(plume, meshY, group, out centre, out halfWidth, out distance);
        }

        // The meshY that lies the given distance down the plume, as GetCrossSection measures it
        public float GetMeshY(float distance)
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetMeshY(plume, distance);
        }

        // How bright the plume is down its middle at meshY, relative to a nozzle, as GetFlameEmission in the shader has it
        public float GetBrightness(float meshY, bool group)
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetBrightness(ref plume, meshY, group);
        }

        // How far into the merge with its neighbours this flame is at meshY: 0 its own jet .. 1 wholly the group's plume, as the shader crossfades them
        public float GetMergeAt(float meshY)
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetMergeAt(plume, meshY);
        }

        // The half-width of the nozzle the plume comes out of, in world space: this flame's own, or the average across the merged group
        public float GetNozzleRadius(bool group)
        {
            GetPlume(out PlumeState plume);
            return PlumeMath.GetNozzleRadius(plume, group);
        }

        // Everything the plume is sampled from, as it is now, for PlumeMath
        public unsafe void GetPlume(out PlumeState plume)
        {
            plume = default;
            FillPlumeParams(ref plume);
            Transform space = GetPlumeSpace(out plume.merge);
            plume.toWorld = space.localToWorldMatrix;
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

        // The renderer the plume is drawn with, and the merge restated in its frame
        Transform GetPlumeSpace(out FlameMerge meshMerge)
        {
            foreach (MeshRef a in meshRenderers)
            {
                if (a.machDiamondsMesh || a.meshRenderer == null)
                    continue;

                Matrix4x4 toModule = transform.worldToLocalMatrix * a.meshRenderer.transform.localToWorldMatrix;
                meshMerge = merge.InSpaceOf(toModule.MultiplyVector(Vector3.right).x, toModule.MultiplyVector(Vector3.up).y);
                return a.meshRenderer.transform;
            }

            meshMerge = merge;
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

        // The widest gap between two rows of a flame mesh, in the shader's meshY (= -vertex.y).
        static readonly Dictionary<Mesh, float> rowSteps = new();

        static float GetRowStep(Mesh mesh)
        {
            if (mesh == null)
                return 0;

            if (rowSteps.TryGetValue(mesh, out float rowStep))
                return rowStep;

            // Can't look, so err on the coarse side: all too wide a step costs is a little overdraw
            rowStep = 0.1f;

            if (mesh.isReadable)
            {
                Vector3[] vertices = mesh.vertices;
                float[] rows = new float[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    rows[i] = -vertices[i].y;
                Array.Sort(rows);

                rowStep = 0;
                for (int i = 1; i < rows.Length; i++)
                    rowStep = Mathf.Max(rowStep, rows[i] - rows[i - 1]);
            }

            rowSteps[mesh] = rowStep;
            return rowStep;
        }

        Bounds ComputeFlameBounds(MeshRef meshRef, ref PlumeState plume, ref FlameMerge merge)
        {
            Mesh mesh = GetMesh(meshRef);
            if (mesh == null)
                return meshRef.meshRenderer.localBounds; // nothing to base it on; leave as-is

            Bounds original = mesh.bounds;

            float maxMeshY = Mathf.Max(Mathf.Abs(original.min.y), Mathf.Abs(original.max.y));
            float maxMeshZ = Mathf.Max(Mathf.Abs(original.min.z), Mathf.Abs(original.max.z));

            PlumeMath.GetFlameBounds(ref plume, ref merge, meshRef.machDiamondsMesh, maxMeshY, maxMeshZ, out Vector3 min, out Vector3 max);

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