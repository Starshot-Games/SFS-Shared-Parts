using System;
using SFS.Variables;
using SFS.World;
using SFS.WorldBase;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class EngineEffects : MonoBehaviour
    {
        #if UNITY_EDITOR
        [BoxGroup("Debug"), Range(0, 1), OnValueChanged(nameof(SetDebug))] public float debugThrottle, debugVacuum, debugNozzleGlow;
        #endif
        
        [Space]
        public Float_Reference engineThrottle;
        public Float_Reference nozzleGlow;
        
        [Space]
        public float glowHeatUpTime = 25;
        public float glowCoolDownTime = 75;

        [Space]
        public FlameMeshModule[] flameMeshes = new FlameMeshModule[0];
        public FlameGlowModule[] flameGlows = new FlameGlowModule[0];
        public NozzleGlow[] nozzleGlows = new NozzleGlow[0];

        [Button(ButtonSizes.Large)]
        void GetModules()
        {
            flameMeshes = GetComponentsInChildren<FlameMeshModule>(true);
            flameGlows = GetComponentsInChildren<FlameGlowModule>(true);
        }

        // Throttle smoothing for mesh
        const float ThrottleSmoothTime = 0.1f;

        // State
        float throttle; // smoothed
        float vacuum;


        void Start() => Clear();
        
        void Update()
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                SetDebug();
                return;
            }
            #endif

            if (GameManager.main == null)
                return;
                
            float atmospherePressure = GetAtmospherePressure();
            vacuum = 1 - atmospherePressure; // physics-based: 0 at the surface (1 bar), 1 in vacuum
            throttle = Mathf.MoveTowards(throttle, engineThrottle.Value, Time.deltaTime / ThrottleSmoothTime);

            foreach (FlameMeshModule m in flameMeshes)
                m.Apply(throttle, vacuum, atmospherePressure);
            foreach (FlameGlowModule g in flameGlows)
                g.Apply(throttle, vacuum);

            // Ease the nozzle glow once here, then push the value to each nozzle to render
            float glowDelta = 1 / (throttle > nozzleGlow.Value ? glowHeatUpTime : glowCoolDownTime) * Time.deltaTime;
            nozzleGlow.Value = Mathf.MoveTowards(nozzleGlow.Value, throttle, glowDelta);
            foreach (NozzleGlow n in nozzleGlows)
                n.Apply(nozzleGlow.Value);
        }

        // Real ambient pressure at the craft, from the planet's own atmosphere model, normalised so
        // the surface = 1 (1 bar) and the top of the atmosphere = 0. Returns 0 (vacuum) with no atmosphere.
        float GetAtmospherePressure()
        {
            if (WorldView.main == null)
                return 0;

            Location location = WorldView.main.ViewLocation;
            Planet planet = location.planet;
            if (planet == null || !planet.HasAtmospherePhysics)
                return 0;

            double surface = planet.GetAtmosphericDensity(0);
            if (surface <= 0)
                return 0;

            return Mathf.Clamp01((float)(planet.GetAtmosphericDensity(location.Height) / surface));
        }
        
        public void Clear()
        {
            throttle = 0;

            foreach (FlameMeshModule m in flameMeshes)
                m.Apply(0, vacuum, 0.5f);
            foreach (FlameGlowModule g in flameGlows)
                g.Apply(0, vacuum);

            nozzleGlow.Value = 0;
            foreach (NozzleGlow n in nozzleGlows)
                n.Apply(0);
        }

        // Drives the editor debug-preview throttle (used by PartVisualizer's flame buttons)
        public void SetDebugThrottle(float throttle)
        {
            #if UNITY_EDITOR
            debugThrottle = throttle;
            SetDebug();
            #endif
        }

        // Drives the editor debug-preview throttle + vacuum (used by PartVisualizer's flame sliders)
        public void SetDebugState(float throttle, float vacuum)
        {
            #if UNITY_EDITOR
            debugThrottle = throttle;
            debugVacuum = vacuum;
            SetDebug();
            #endif
        }


        #if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying)
                SetDebug();
        }

        void SetDebug()
        {
            if (Application.isPlaying)
                return;
            
            foreach (FlameMeshModule m in flameMeshes)
            {
                m.debugThrottle = debugThrottle;
                m.debugVacuum = debugVacuum;
                m.ApplyDebug(debugThrottle, debugVacuum);
            }
            foreach (FlameGlowModule g in flameGlows)
            {
                g.debugThrottle = debugThrottle;
                g.debugVacuum = debugVacuum;
                g.Apply(debugThrottle, debugVacuum);
            }

            nozzleGlow.Value = debugNozzleGlow;
            foreach (NozzleGlow n in nozzleGlows)
                n.Apply(debugNozzleGlow);
        }
        #endif
    }

    // Renders a glow value (the easing + saved value live on EngineEffects) onto the _GlowIntensity shader prop.
    [Serializable]
    public class NozzleGlow
    {
        static readonly int GlowIntensity = Shader.PropertyToID("_GlowIntensity");

        public GlowMesh[] meshes;
        
        public void Apply(float glow)
        {
            if (meshes == null)
                return;

            float intensity = Mathf.Pow(glow, 2.2f);
            foreach (GlowMesh mesh in meshes)
            {
                if (mesh == null || mesh.mesh == null)
                    continue;

                MeshRenderer meshRenderer = mesh.mesh;
                const int Index = 0; // Wont work with submeshes, ok with it for now

                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                if (meshRenderer.HasPropertyBlock())
                    meshRenderer.GetPropertyBlock(propertyBlock, Index);

                propertyBlock.SetFloat(GlowIntensity, intensity * mesh.intensityMultiplier);
                meshRenderer.SetPropertyBlock(propertyBlock, Index);
            }
        }
        
        [Serializable]
        public class GlowMesh
        {
            public MeshRenderer mesh;
            public float intensityMultiplier = 1;
        }
    }
}
