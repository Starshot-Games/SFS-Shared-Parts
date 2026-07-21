using System;
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

        void OnValidate()
        {
            flameExitTemperature = Mathf.Pow(exitPressure, (Gamma - 1f) / Gamma); // isentropic T/Tc = (P/Pc)^((g-1)/g)

            #if UNITY_EDITOR
            atmospherePressureBar = DebugAtmospherePressure(debugVacuum);
            if (!Application.isPlaying && meshRenderers != null)
                ApplyDebug(debugThrottle, debugVacuum);
            #endif
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

                propertyBlock.SetFloat(Throttle, throttle); // * Mathf.Lerp(1, 0.75f, vacuum));

                propertyBlock.SetFloat(AdditiveBlend, Mathf.Lerp(groundData.additiveBlend, vacuumData.additiveBlend, vacuum));
                
                propertyBlock.SetFloat("exitPressure", exitPressure); // nozzle-exit static pressure (bar)

                propertyBlock.SetFloat("atmospherePressure", atmospherePressure); // 1 bar at ground -> 0 in vacuum, falling off exponentially with height

                propertyBlock.SetFloat("isMachDiamondsShader", a.machDiamondsMesh? 1 : 0);
                
                propertyBlock.SetFloat(StripesWidth, stripesWidth);
                propertyBlock.SetFloat(StripesStrength, stripesStrength);

                a.meshRenderer.SetPropertyBlock(propertyBlock, 0);

                // Frustum-culling fix: the vertex shader (Flame_Base.cginc / VertCore) displaces every
                // vertex far outside the source mesh (width *= GetFlameWidth() - up to ~20x in the
                // near-vacuum billow, height *= 12*exitPressure*3, then everything *= 2). Unity culls on
                // the *source* mesh bounds, so without this it wrongly culls the flame as soon as the tiny
                // base mesh leaves the frustum. Override localBounds with the displaced envelope so culling
                // matches what is actually drawn.
                a.meshRenderer.localBounds = ComputeFlameBounds(a, throttle, atmospherePressure);
            }
        }

        // Local-space AABB of the shader-displaced flame. Mirrors VertCore + GetFlameWidth so culling
        // tracks the real drawn envelope; kept intentionally conservative (small safety margin) since an
        // over-large bound only costs the odd extra draw, while an under-sized one reintroduces the bug.
        Bounds ComputeFlameBounds(MeshRef a, float throttle, float atmospherePressure)
        {
            if (a.meshFilter == null)
                a.meshFilter = a.meshRenderer.GetComponent<MeshFilter>();

            Mesh mesh = a.meshFilter != null ? a.meshFilter.sharedMesh : null;
            if (mesh == null)
                return a.meshRenderer.localBounds; // nothing to base it on; leave as-is

            Bounds b = mesh.bounds;

            // Width envelope is monotonic in meshY (= -vertex.y), so it peaks at the most-displaced tip.
            float maxMeshY = Mathf.Max(Mathf.Abs(b.min.y), Mathf.Abs(b.max.y));
            float width = GetFlameWidth(maxMeshY, throttle, atmospherePressure) * 1.1f; // +10% margin

            // xz get scaled by width then *2 (VertCore: a.vertex.xz *= width; a.vertex *= 2).
            float halfX = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x)) * 2f * width;
            float halfZ = Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z)) * 2f * width;

            // y gets scaled by 12*exitPressure*3 then *2.
            float yScale = 12f * exitPressure * 3f * 2f;
            float y0 = b.min.y * yScale;
            float y1 = b.max.y * yScale;

            Bounds local = new Bounds();
            local.SetMinMax(
                new Vector3(-halfX, Mathf.Min(y0, y1), -halfZ),
                new Vector3(halfX, Mathf.Max(y0, y1), halfZ));
            return local;
        }

        // CPU port of the shader's GetDiamondsStrength (Flame_Base.cginc): overexpansion-driven,
        // zero once the jet reaches perfect expansion.
        static float GetDiamondsStrength(float flamePressure, float ambientPressure)
            => Mathf.Clamp01(Mathf.Max(ambientPressure - flamePressure, 0f) * 2f);

        // CPU port of the shader's GetFlameWidth (Flame_Base.cginc). Keep in sync with the shader.
        float GetFlameWidth(float y, float throttle, float atmospherePressure)
        {
            const float gamma = 1.2f;
            const int diamondCount = 5;
            const float diamondOffset = 0f; // material default; C# never overrides it

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