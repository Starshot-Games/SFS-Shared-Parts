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
            GlareAxis = Shader.PropertyToID("glareAxis"),
            GlareShape = Shader.PropertyToID("glareShape"),
            GlareFlow = Shader.PropertyToID("glareFlow"),
            DiamondOffsetProperty = Shader.PropertyToID("diamondOffset"),
            FlameColorProperty = Shader.PropertyToID("_FlameColor");
        
        
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
        [NonSerialized] public FlameGlare[] glare = new FlameGlare[FlameMergeSolver.MaxGlareSources];
        [NonSerialized] public int glareCount;
        bool registered;

        // Scratch for handing the glare to the shader, which takes it as arrays. Always sent whole:
        // a property block fixes an array's length the first time it is set.
        static readonly Vector4[] glareAxis = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] glareShape = new Vector4[FlameMergeSolver.MaxGlareSources];
        static readonly Vector4[] glareFlow = new Vector4[FlameMergeSolver.MaxGlareSources];

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

                a.meshRenderer.SetPropertyBlock(propertyBlock, 0);
                
                // Fix for frustum culling
                // Note: This has to be updated along with the flame shader
                a.meshRenderer.localBounds = ComputeFlameBounds(a, meshMerge, throttle, atmospherePressure);
            }
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

            float approach = merge.alongOffset > 1e-4f ? Mathf.Clamp01(along / merge.alongOffset) : 1;
            float planeLeft = mergeCentre - merge.halfSpan + 2 * merge.halfSpan * merge.share.x;
            float planeRight = mergeCentre - merge.halfSpan + 2 * merge.halfSpan * merge.share.y;
            Vector2 nozzleBounds = merge.nozzleBounds - Vector2.one * (merge.drift * along);
            bool merged = along >= merge.alongOffset;

            left = outerLeft
                ? Mathf.Lerp(left, sliceLeft, m)
                : (merged ? sliceLeft : Mathf.Max(left, Mathf.Lerp(nozzleBounds.x, planeLeft, approach)));
            right = outerRight
                ? Mathf.Lerp(right, sliceRight, m)
                : (merged ? sliceRight : Mathf.Min(right, Mathf.Lerp(nozzleBounds.y, planeRight, approach)));

            // A covered outer slice closes up rather than inverting
            if (outerLeft)
                left = Mathf.Min(left, right);
            if (outerRight)
                right = Mathf.Max(right, left);

            // At a join with a neighbouring slice the geometry overshoots, and the join itself is
            // settled per fragment (GetSliceCover in the shader).
            float overshoot = (CoverMargin + JoinFeather) * merge.halfSpan;
            if (!outerLeft)
                left -= overshoot;
            if (!outerRight)
                right += overshoot;
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