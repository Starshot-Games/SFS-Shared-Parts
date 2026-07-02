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
            }
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
        }
    }
}