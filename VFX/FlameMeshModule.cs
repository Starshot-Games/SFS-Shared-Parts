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
            MergeFadeLength = Shader.PropertyToID("mergeFadeLength"),
            MergeShare = Shader.PropertyToID("mergeShare"),
            MergeLitShare = Shader.PropertyToID("mergeLitShare"),
            MergeShareFadeLength = Shader.PropertyToID("mergeShareFadeLength"),
            MergeNozzleBounds = Shader.PropertyToID("mergeNozzleBounds"),
            MergeDrift = Shader.PropertyToID("mergeDrift"),
            MergeStripeScale = Shader.PropertyToID("mergeStripeScale"),
            MergeRowStep = Shader.PropertyToID("mergeRowStep"),
            MergeLead = Shader.PropertyToID("mergeLead"),
            MergeRefLength = Shader.PropertyToID("mergeRefLength"),
            NeighbourFrame = Shader.PropertyToID("neighbourFrame"),
            NeighbourFlow = Shader.PropertyToID("neighbourFlow"),
            MergeCenter = Shader.PropertyToID("mergeCenter"),
            MergeHalfSpan = Shader.PropertyToID("mergeHalfSpan"),
            MergeThroat = Shader.PropertyToID("mergeThroat"),
            MergeGroupLength = Shader.PropertyToID("mergeGroupLength"),
            MergeAlongOffset = Shader.PropertyToID("mergeAlongOffset"),
            MergeExitPressure = Shader.PropertyToID("mergeExitPressure"),
            MergeThrottle = Shader.PropertyToID("mergeThrottle"),
            MergeColor = Shader.PropertyToID("mergeColor"),
            MergeAdditiveBlend = Shader.PropertyToID("mergeAdditiveBlend"),
            MergeStripesWidth = Shader.PropertyToID("mergeStripesWidth"),
            MergeStripesStrength = Shader.PropertyToID("mergeStripesStrength"),
            MergeTurbulence = Shader.PropertyToID("mergeTurbulence"),
            MergeMirror = Shader.PropertyToID("mergeMirror"),
            MergeSeed = Shader.PropertyToID("mergeSeed"),
            NoiseSeed = Shader.PropertyToID("noiseSeed"),
            EddyOffset = Shader.PropertyToID("eddyOffset"),
            MergeEddyOffset = Shader.PropertyToID("mergeEddyOffset"),
            EddyFlicker = Shader.PropertyToID("eddyFlicker"),
            TurbulenceProperty = Shader.PropertyToID("_Turbulence"),
            RoughnessProperty = Shader.PropertyToID("_Roughness"),
            GlareAxis = Shader.PropertyToID("glareAxis"),
            GlareShape = Shader.PropertyToID("glareShape"),
            GlareFlow = Shader.PropertyToID("glareFlow"),
            DiamondOffsetProperty = Shader.PropertyToID("diamondOffset"),
            FlameColorProperty = Shader.PropertyToID("_FlameColor"),
            FlameMaskProperty = Shader.PropertyToID("_FlameMask");
        
        
        // Debug preview - refreshes on validate. EngineEffects drives this at runtime.
        #if UNITY_EDITOR
        [BoxGroup("Debug"), Range(0, 1)] public float debugThrottle, debugVacuum;
        #endif
        [BoxGroup("Debug"), ReadOnly, ShowInInspector] float atmospherePressureBar; // current ambient pressure (bar), updated each Apply
        
        const float Gamma = 1.2f; // ratio of specific heats for hot combustion products

        [Space]
        public float exitPressure = 0.5f; // nozzle-exit static pressure (bar)
        [ReadOnly, ShowInInspector] float flameExitTemperature; // fraction of chamber temperature (1 = chamber)

        [Space]
        public float stripesWidth = 1;
        [Range(0, 3)] public float stripesStrength = 1;

        // How much light this flame gives off next to other engines'.
        [Space]
        [Range(0, 1)] public float mergeLuminosity = 1;

        // Plume merging. The solver reads these off every live flame and writes back the slice of
        // the shared plume this one draws, and the far brighter plumes around that drown it out
        // wherever it lies under them.
        [NonSerialized] public float appliedThrottle;
        [NonSerialized] public float appliedAtmospherePressure;
        [NonSerialized] public float appliedAdditiveBlend;
        [NonSerialized] public FlameMerge merge;
        [NonSerialized] public SmokeTrailModule smokeTrail; // set by the smoke module while it's on, so a merged group can pick one to send out its smoke
        [NonSerialized] public FlameGlare[] glare = new FlameGlare[FlameMergeSolver.MaxGlareSources];
        [NonSerialized] public int glareCount;
        bool registered;

        // Scratch for handing the glare to the shader, which takes it as arrays. Always sent whole:
        // a property block fixes an array's length the first time it is set.
        static readonly Vector4[] glareAxis = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] glareShape = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] glareFlow = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] neighbourFrame = new Vector4[2];
        static readonly Vector4[] neighbourFlow = new Vector4[2];

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

        // Where in the turbulence pattern this flame sits, so engines side by side don't churn in step
        [NonSerialized] public float noiseSeed;
        void Awake() => noiseSeed = UnityEngine.Random.value;

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

            // Set
            foreach (MeshRef a in meshRenderers)
            {
                // Disable the mesh entirely when the engine is off
                GameObject go = a.meshRenderer.gameObject;
                if (go.activeSelf != on)
                    go.SetActive(on);

                if (!on)
                    continue;

                MaterialPropertyBlock propertyBlock = new();

                // The merge is solved in module space; the shader runs in the renderer's, and a mesh
                // child can sit rotated or scaled relative to the module.
                Matrix4x4 toModule = transform.worldToLocalMatrix * a.meshRenderer.transform.localToWorldMatrix;
                float acrossScale = toModule.MultiplyVector(Vector3.right).x;
                float alongScale = toModule.MultiplyVector(Vector3.up).y;
                FlameMerge meshMerge = merge.InSpaceOf(acrossScale, alongScale);

                propertyBlock.SetFloat(Throttle, throttle); // * Mathf.Lerp(1, 0.75f, vacuum));

                propertyBlock.SetFloat(AdditiveBlend, additiveBlend);
                
                propertyBlock.SetFloat("exitPressure", exitPressure); // nozzle-exit static pressure (bar)

                propertyBlock.SetFloat("atmospherePressure", atmospherePressure); // 1 bar at ground -> 0 in vacuum, falling off exponentially with height

                propertyBlock.SetFloat("isMachDiamondsShader", a.machDiamondsMesh? 1 : 0);
                
                propertyBlock.SetFloat(StripesWidth, stripesWidth);
                propertyBlock.SetFloat(StripesStrength, stripesStrength);

                // The far brighter plumes this flame is drowned out under (a depth of 0 = none there)
                for (int i = 0; i < glareAxis.Length; i++)
                {
                    FlameGlare g = i < glareCount ? glare[i].InSpaceOf(acrossScale, alongScale) : default;

                    glareAxis[i] = new Vector4(g.origin.x, g.origin.y, g.slope, g.depth);
                    glareShape[i] = new Vector4(g.halfSpan, g.throat, g.length);
                    glareFlow[i] = new Vector4(g.exitPressure, g.throttle);
                }
                propertyBlock.SetVectorArray(GlareAxis, glareAxis);
                propertyBlock.SetVectorArray(GlareShape, glareShape);
                propertyBlock.SetVectorArray(GlareFlow, glareFlow);

                // Merged plume (mergeAmount 0 = not merging, and the shader draws it as it always did)
                propertyBlock.SetFloat(MergeAmount, meshMerge.amount);
                propertyBlock.SetFloat(MergeFadeLength, meshMerge.fadeLength);
                propertyBlock.SetVector(MergeShare, meshMerge.share);
                propertyBlock.SetVector(MergeLitShare, meshMerge.litShare);
                propertyBlock.SetFloat(MergeShareFadeLength, meshMerge.shareFadeLength);
                propertyBlock.SetVector(MergeNozzleBounds, meshMerge.nozzleBounds);
                propertyBlock.SetFloat(MergeDrift, meshMerge.drift);
                propertyBlock.SetFloat(MergeStripeScale, meshMerge.stripeScale);
                propertyBlock.SetFloat(MergeRowStep, GetRowStep(GetMesh(a)));
                propertyBlock.SetFloat(MergeLead, meshMerge.lead);
                propertyBlock.SetFloat(MergeRefLength, meshMerge.refLength);
                SetNeighbour(0, meshMerge.left, throttle);
                SetNeighbour(1, meshMerge.right, throttle);
                propertyBlock.SetVectorArray(NeighbourFrame, neighbourFrame);
                propertyBlock.SetVectorArray(NeighbourFlow, neighbourFlow);
                propertyBlock.SetFloat(MergeCenter, meshMerge.center);
                propertyBlock.SetFloat(MergeHalfSpan, meshMerge.halfSpan);
                propertyBlock.SetFloat(MergeThroat, meshMerge.throat);
                propertyBlock.SetFloat(MergeGroupLength, meshMerge.groupLength);
                propertyBlock.SetFloat(MergeAlongOffset, meshMerge.alongOffset);
                propertyBlock.SetFloat(MergeExitPressure, meshMerge.exitPressure);
                propertyBlock.SetFloat(MergeThrottle, meshMerge.throttle);
                propertyBlock.SetColor(MergeColor, meshMerge.color);
                propertyBlock.SetFloat(MergeAdditiveBlend, meshMerge.additiveBlend);
                propertyBlock.SetFloat(MergeStripesWidth, meshMerge.stripesWidth);
                propertyBlock.SetFloat(MergeStripesStrength, meshMerge.stripesStrength);
                propertyBlock.SetVector(MergeTurbulence, meshMerge.turbulence);
                propertyBlock.SetFloat(MergeMirror, meshMerge.mirror);
                propertyBlock.SetFloat(MergeSeed, meshMerge.seed);
                propertyBlock.SetFloat(NoiseSeed, noiseSeed);

                Transform space = a.meshRenderer.transform;
                float flow = EddySpeed * space.TransformVector(Vector3.right).magnitude / (GetPlumeLength(exitPressure) * Mathf.Max(space.TransformVector(Vector3.up).magnitude, 1e-6f));
                propertyBlock.SetVector(EddyOffset, GetEddyOffset(flow, Time.timeAsDouble));
                propertyBlock.SetVector(MergeEddyOffset, GetEddyOffset(meshMerge.eddyFlow, Time.timeAsDouble));
                propertyBlock.SetVector(EddyFlicker, GetEddyFlicker(Time.timeAsDouble));

                a.meshRenderer.SetPropertyBlock(propertyBlock, 0);
                
                // Fix for frustum culling
                // Note: This has to be updated along with the flame shader
                a.meshRenderer.localBounds = ComputeFlameBounds(a, meshMerge, throttle, atmospherePressure);
            }
        }
        
        // A missing neighbour still goes in as a valid jet, so the shader never divides by zero working it out
        void SetNeighbour(int i, FlameNeighbour n, float throttle)
        {
            neighbourFrame[i] = n.present ? new Vector4(n.origin.x, n.origin.y, n.scale.x, n.scale.y) : new Vector4(0, 0, 1, 1);
            neighbourFlow[i] = n.present ? new Vector4(n.slope, n.exitPressure, n.throttle, 1) : new Vector4(0, exitPressure, throttle, 0);
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

        // How far down the mesh (0 at the nozzle, 1 at its far end) the drawn plume has faded out, as the shader shortens it with throttle
        // How far down the mesh (0 at the nozzle, 1 at its far end) the plume has faded out, as the shader shortens it with throttle: this jet, or the group's merged plume
        public float GetFadedOutMeshY(bool group) => Mathf.Lerp(0.01f, 1, group && merge.amount > 0 ? merge.throttle : appliedThrottle);

        // The plume at meshY in world space - this jet, or the group's merged plume - as its middle, half-width, and how far down it that is
        public void GetCrossSection(float meshY, bool group, out Vector2 centre, out float halfWidth, out float distance)
        {
            Transform space = GetPlumeSpace(out FlameMerge meshMerge);
            PlumeSample sample = SamplePlume(meshY, meshMerge, group);

            centre = space.TransformPoint(new Vector3(sample.centre, -sample.along));
            halfWidth = sample.halfWidth * space.TransformVector(Vector3.right).magnitude;
            distance = sample.along * space.TransformVector(Vector3.up).magnitude;
        }

        // The meshY that lies the given distance down the plume, as GetCrossSection measures it
        public float GetMeshY(float distance)
        {
            Transform space = GetPlumeSpace(out FlameMerge meshMerge);
            float ownLength = GetPlumeLength(exitPressure);
            float extra = meshMerge.amount > 0 ? Mathf.Max(meshMerge.alongOffset + meshMerge.groupLength - ownLength, 0) : 0;
            float along = Mathf.Max(distance, 0) / Mathf.Max(space.TransformVector(Vector3.up).magnitude, 1e-6f);

            // along = meshY · (ownLength + extra · meshY), solved for meshY
            return 2 * along / (ownLength + Mathf.Sqrt(ownLength * ownLength + 4 * extra * along));
        }

        // How bright the plume is down its middle at meshY, relative to a nozzle, as GetFlameEmission in the shader has it (T^4 going as width^-1.6).
        // The group's merged plume comes out the same for every flame in it.
        public float GetBrightness(float meshY, bool group)
        {
            GetPlumeSpace(out FlameMerge meshMerge);
            PlumeSample sample = SamplePlume(meshY, meshMerge, group);

            float lengthFade = Mathf.Clamp01(1 - meshY / GetFadedOutMeshY(group));
            return Mathf.Pow(Mathf.Max(sample.widthRatio, 1e-4f), -1.6f) * lengthFade * GetMaskAlong(meshY);
        }

        // How far into the merge with its neighbours this flame is at meshY: 0 its own jet .. 1 wholly the group's plume, as the shader crossfades them
        public float GetMergeAt(float meshY)
        {
            GetPlumeSpace(out FlameMerge meshMerge);
            if (meshMerge.amount <= 0)
                return 0;

            float along = SamplePlume(meshY, meshMerge, false).along;
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(meshMerge.alongOffset, meshMerge.alongOffset + meshMerge.fadeLength, along)) * meshMerge.amount;
        }

        // The half-width of the nozzle the plume comes out of, in world space: this flame's own, or the average across the merged group
        public float GetNozzleRadius(bool group)
        {
            Transform space = GetPlumeSpace(out FlameMerge meshMerge);
            return (group && meshMerge.amount > 0 ? meshMerge.nozzleRadius : 1) * space.TransformVector(Vector3.right).magnitude;
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

        struct PlumeSample
        {
            public float along, centre, halfWidth, widthRatio;
        }

        // The plume at meshY, before GetPlumeShape cuts it into slices: this jet on its own, or the group's whole envelope, which every flame in it agrees on
        PlumeSample SamplePlume(float meshY, FlameMerge m, bool group)
        {
            float ownLength = GetPlumeLength(exitPressure);
            bool merging = m.amount > 0;
            float mergedLength = merging ? Mathf.Max(m.alongOffset + m.groupLength, ownLength) : ownLength;

            PlumeSample sample;
            sample.along = meshY * (ownLength + (mergedLength - ownLength) * meshY);
            float ownWidth = GetFlameWidth(Mathf.Clamp01(sample.along / ownLength), exitPressure, appliedThrottle, appliedAtmospherePressure, DiamondOffset);
            sample.centre = 0;
            sample.halfWidth = sample.widthRatio = ownWidth;
            if (!merging || !group)
                return sample;

            float groupT = Mathf.Clamp01((sample.along - m.alongOffset) / Mathf.Max(m.groupLength, 1e-4f));
            float groupWidth = m.halfSpan + m.throat * (GetFlameWidth(groupT, m.exitPressure, m.throttle, appliedAtmospherePressure, DiamondOffset) - 1);

            sample.centre = m.center - m.drift * sample.along;
            sample.halfWidth = groupWidth;
            sample.widthRatio = groupWidth / Mathf.Max(m.throat, 1e-4f);
            return sample;
        }

        // The flame mask's alpha down the plume's middle, which the shader samples at 1 - v. It can't be read on the CPU, so it's read back once per texture.
        static readonly Dictionary<Texture, float[]> maskProfiles = new();
        float[] maskProfile;

        float GetMaskAlong(float meshY)
        {
            if (maskProfile == null)
            {
                Material material = GetPlumeMaterial(FlameMaskProperty);
                maskProfile = GetMaskProfile(material != null ? material.GetTexture(FlameMaskProperty) : null);
            }

            float x = Mathf.Clamp((1 - meshY) * maskProfile.Length - 0.5f, 0, maskProfile.Length - 1);
            int i = Mathf.Min((int)x, maskProfile.Length - 2);
            return Mathf.Lerp(maskProfile[i], maskProfile[i + 1], x - i);
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
            Destroy(readback);

            maskProfiles[mask] = profile;
            return profile;
        }

        static Mesh GetMesh(MeshRef meshRef)
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

        Bounds ComputeFlameBounds(MeshRef meshRef, FlameMerge merge, float throttle, float atmospherePressure)
        {
            Mesh mesh = GetMesh(meshRef);
            if (mesh == null)
                return meshRef.meshRenderer.localBounds; // nothing to base it on; leave as-is

            Bounds original = mesh.bounds;

            float maxMeshY = Mathf.Max(Mathf.Abs(original.min.y), Mathf.Abs(original.max.y));
            float maxMeshZ = Mathf.Max(Mathf.Abs(original.min.z), Mathf.Abs(original.max.z));

            // Merged, the slice slides sideways as well as widening and is no longer widest at the
            // tip, so walk down the plume instead of only checking the far end.
            const int samples = 8;
            float halfX = 0, halfZ = 0, reach = 0;

            for (int i = 0; i <= samples; i++)
            {
                GetPlumeShape(maxMeshY * i / samples, meshRef.machDiamondsMesh, merge, throttle, atmospherePressure, out float left, out float right, out float along);

                halfX = Mathf.Max(halfX, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
                halfZ = Mathf.Max(halfZ, maxMeshZ * Mathf.Abs(right - left));
                reach = Mathf.Max(reach, along);
            }

            const float margin = 1.1f; // +10%

            Bounds result = new Bounds();
            result.SetMinMax(
                new Vector3(-halfX * margin, -reach * margin, -halfZ * margin),
                new Vector3(halfX * margin, reach * (margin - 1), halfZ * margin));
            return result;
        }

        // Mirror of GetPlumeShape in the flame shader, for the culling bounds above
        void GetPlumeShape(float meshY, bool machDiamondsMesh, FlameMerge merge, float throttle, float atmospherePressure, out float left, out float right, out float along)
        {
            float ownLength = GetPlumeLength(exitPressure);
            bool takesShare = merge.amount > 0 && !machDiamondsMesh;

            float mergedLength = takesShare ? Mathf.Max(merge.alongOffset + merge.groupLength, ownLength) : ownLength;
            along = meshY * (ownLength + (mergedLength - ownLength) * meshY);

            float ownWidth = GetFlameWidth(Mathf.Clamp01(along / ownLength), exitPressure, throttle, atmospherePressure, DiamondOffset);
            left = -ownWidth;
            right = ownWidth;

            if (!takesShare)
                return;

            float m = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(merge.alongOffset, merge.alongOffset + merge.fadeLength, along)) * merge.amount;

            float groupT = Mathf.Clamp01((along - merge.alongOffset) / Mathf.Max(merge.groupLength, 1e-4f));
            float groupWidth = merge.halfSpan + merge.throat * (GetFlameWidth(groupT, merge.exitPressure, merge.throttle, atmospherePressure, DiamondOffset) - 1);

            float mergeCentre = merge.center - merge.drift * along;
            float envelopeLeft = mergeCentre - groupWidth;
            float envelopeRight = mergeCentre + groupWidth;

            bool outerLeft = merge.share.x <= 1e-4f;
            bool outerRight = merge.share.y >= 1 - 1e-4f;

            // The joins hold the places they have at the merge plane; only the outline goes with the envelope
            Vector2 share = Vector2.Lerp(merge.share, merge.litShare, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(merge.alongOffset, merge.alongOffset + Mathf.Max(merge.shareFadeLength, 1e-4f), along)));
            float sliceLeft = outerLeft ? envelopeLeft : Mathf.Clamp(mergeCentre - merge.halfSpan + 2 * merge.halfSpan * share.x, envelopeLeft, envelopeRight);
            float sliceRight = outerRight ? envelopeRight : Mathf.Clamp(mergeCentre - merge.halfSpan + 2 * merge.halfSpan * share.y, envelopeLeft, envelopeRight);

            // A pair is only split below the lower of its two nozzles
            float startLeft = merge.left.present ? Mathf.Max(merge.left.origin.y, 0) : 0;
            float startRight = merge.right.present ? Mathf.Max(merge.right.origin.y, 0) : 0;
            float approachLeft = merge.alongOffset > startLeft + 1e-4f ? Mathf.Clamp01((along - startLeft) / (merge.alongOffset - startLeft)) : 1;
            float approachRight = merge.alongOffset > startRight + 1e-4f ? Mathf.Clamp01((along - startRight) / (merge.alongOffset - startRight)) : 1;
            float planeLeft = mergeCentre - merge.halfSpan + 2 * merge.halfSpan * merge.share.x;
            float planeRight = mergeCentre - merge.halfSpan + 2 * merge.halfSpan * merge.share.y;
            Vector2 nozzleBounds = merge.nozzleBounds - Vector2.one * (merge.drift * along);
            bool merged = along >= merge.alongOffset;
            bool joinLeft = !outerLeft && along >= startLeft;
            bool joinRight = !outerRight && along >= startRight;

            left = outerLeft
                ? Mathf.Lerp(left, sliceLeft, m)
                : (!joinLeft ? left : (merged ? sliceLeft : Mathf.Lerp(nozzleBounds.x, planeLeft, approachLeft)));
            right = outerRight
                ? Mathf.Lerp(right, sliceRight, m)
                : (!joinRight ? right : (merged ? sliceRight : Mathf.Lerp(nozzleBounds.y, planeRight, approachRight)));

            // A covered outer slice closes up rather than inverting
            if (outerLeft)
                left = Mathf.Min(left, right);
            if (outerRight)
                right = Mathf.Max(right, left);

            // Away from its joins a slice also draws its neighbours' light, as far as it reaches (GetReach in the shader)
            if (!joinLeft)
                left = Mathf.Min(left, Mathf.Min(GetNeighbourEdge(merge.left, along, atmospherePressure, -1), GetNeighbourEdge(merge.right, along, atmospherePressure, -1)));
            if (!joinRight)
                right = Mathf.Max(right, Mathf.Max(GetNeighbourEdge(merge.left, along, atmospherePressure, 1), GetNeighbourEdge(merge.right, along, atmospherePressure, 1)));

            // At a join with a neighbouring slice the geometry overshoots, and the join itself is
            // settled per fragment (GetSliceCover in the shader).
            float overshoot = (CoverMargin + JoinFeather) * merge.halfSpan;
            if (!outerLeft)
                left -= overshoot;
            if (!outerRight)
                right += overshoot;
        }

        // A neighbouring jet's edge on one side (-1 left, 1 right) at a distance down this flame; none upstream of its nozzle
        float GetNeighbourEdge(FlameNeighbour n, float along, float atmospherePressure, float side)
        {
            float alongN = (along - n.origin.y) / Mathf.Max(n.scale.y, 1e-6f);
            if (!n.present || alongN < 0)
                return -side * float.MaxValue;

            float width = GetFlameWidth(Mathf.Clamp01(alongN / GetPlumeLength(n.exitPressure)), n.exitPressure, n.throttle, atmospherePressure, DiamondOffset);
            return n.origin.x + n.slope * along + side * width * Mathf.Abs(n.scale.x);
        }

        // coverMargin and joinFeather in the flame shader
        const float CoverMargin = 0.03f, JoinFeather = 0.02f;

        // The plume's drawn length in local units (matches the y scale in VertCore).
        public static float GetPlumeLength(float exitPressure) => 12 * exitPressure * 3 * 2;

        static float GetDiamondsStrength(float flamePressure, float ambientPressure)
            => Mathf.Clamp01(Mathf.Max(ambientPressure - flamePressure, 0f) * 2f);

        public static float GetFlameWidth(float y, float exitPressure, float throttle, float atmospherePressure, float diamondOffset)
        {
            const float gamma = 1.2f;
            const int diamondCount = 5;

            float flamePressure = exitPressure * Mathf.Lerp(0.1f, 1f, throttle);
            float ambientPressure = Mathf.Max(atmospherePressure, 1e-3f);

            float width = 0f;

            // Mach diamonds
            float diamonds = Mathf.Abs(Mathf.Sin((y * diamondCount - diamondOffset) * Mathf.PI))
                             - Mathf.Abs(Mathf.Sin(-diamondOffset * Mathf.PI));
            width += diamonds * GetDiamondsStrength(flamePressure, ambientPressure) * 0.3f;

            // Standard expansion
            width += y * 0.5f;

            // Vac / underexpanded billow
            float pressureRatio = flamePressure / ambientPressure;
            float expandWidth = Mathf.Pow(Mathf.Max(pressureRatio, 1f), 1f / (2f * gamma)) - 1f;
            width += expandWidth * (Mathf.Sqrt(y + 0.05f) - Mathf.Sqrt(0.05f)) * 1.3f;

            // Scale (based on pressure)
            width *= exitPressure * 3f;

            return 1f + width;
        }

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
        }
    }
}