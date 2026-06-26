using System;
using System.Linq;
using SFS.Builds;
using SFS.Variables;
using SFS.World;
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

        // Throttle smoothing for mesh
        const float ThrottleSmoothTime = 0.1f;

        // State
        float throttle; // smoothed
        float vacuum;


        void Start() => RunStart();

        public void RunStart()
        {
            Clear();
        }

        void Update()
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                SetDebug();
                return;
            }
            #endif

            if (GameManager.main == null && BuildManager.main == null)
                return;
                
            vacuum = GetVacuum();
            throttle = Mathf.MoveTowards(throttle, engineThrottle.Value, Time.deltaTime / ThrottleSmoothTime);

            foreach (FlameMeshModule m in flameMeshes)
                m.Apply(throttle, vacuum);
            foreach (FlameGlowModule g in flameGlows)
                g.Apply(throttle, vacuum);

            // Ease the nozzle glow once here, then push the value to each nozzle to render
            float glowDelta = 1 / (throttle > nozzleGlow.Value ? glowHeatUpTime : glowCoolDownTime) * Time.deltaTime;
            nozzleGlow.Value = Mathf.MoveTowards(nozzleGlow.Value, throttle, glowDelta);
            foreach (NozzleGlow n in nozzleGlows)
                n.Apply(nozzleGlow.Value);
        }

        float GetVacuum()
        {
            Location location = WorldView.main.ViewLocation;
            return Mathf.InverseLerp(0.05f, 0.8f, (float)(location.Height / location.planet.AtmosphereHeightPhysics));
        }
        
        public void Clear()
        {
            throttle = 0;

            foreach (FlameMeshModule m in flameMeshes)
                m.Apply(0, vacuum);
            foreach (FlameGlowModule g in flameGlows)
                g.Apply(0, vacuum);

            nozzleGlow.Value = 0;
            foreach (NozzleGlow n in nozzleGlows)
                n.Apply(0);
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
                m.Apply(debugThrottle, debugVacuum);
            }
            foreach (FlameGlowModule g in flameGlows)
                g.Apply(debugThrottle, debugVacuum);

            nozzleGlow.Value = debugNozzleGlow;
            foreach (NozzleGlow n in nozzleGlows)
                n.Apply(debugNozzleGlow);
        }

        // Porting tool: pull data out of the legacy components found under this part and rebuild the
        // EngineEffects arrays from them. Run once per part, then remove the old components.
        [PropertySpace, HorizontalGroup("Legacy"), Button(ButtonSizes.Large, Name = "Copy From Legacy Components")]
        void CopyFromLegacy()
        {
            nozzleGlows = GetComponentsInChildren<NozzleGlowModule>(true).Select(m => new NozzleGlow(m)).ToArray();

            // Throttle variable lived on NozzleGlowModule.engineThrottle in the legacy setup
            NozzleGlowModule legacyNozzle = GetComponentInChildren<NozzleGlowModule>(true);
            if (legacyNozzle != null)
            {
                engineThrottle = legacyNozzle.engineThrottle;
                nozzleGlow = legacyNozzle.glowIntensity; // glow save reference now lives on the parent
                glowHeatUpTime = legacyNozzle.heatUpTime;
                glowCoolDownTime = legacyNozzle.coolDownTime;
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"EngineEffects: copied {flameMeshes.Length} flame mesh(es), {flameGlows.Length} glow(s), {nozzleGlows.Length} nozzle glow(s) from legacy components.");
        }

        // Strips the now-redundant legacy driver components. FlameRandomizer is intentionally kept.
        [HorizontalGroup("Legacy"), Button(ButtonSizes.Large, Name = "Remove Legacy Components")]
        void RemoveLegacy()
        {
            int removed = 0;
            
            foreach (NozzleGlowModule m in GetComponentsInChildren<NozzleGlowModule>(true)) { DestroyImmediate(m); removed++; }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"EngineEffects: removed {removed} legacy component(s) (FlameRandomizer kept).");
        }
        #endif
    }

    // Renders a glow value (the easing + saved value live on EngineEffects) onto the _GlowIntensity shader prop.
    [Serializable]
    public class NozzleGlow
    {
        static readonly int GlowIntensity = Shader.PropertyToID("_GlowIntensity");

        public NozzleGlowModule.GlowMesh[] meshes;

        public NozzleGlow() { }
        public NozzleGlow(NozzleGlowModule m) => meshes = m.meshes;

        public void Apply(float glow)
        {
            if (meshes == null)
                return;

            float intensity = Mathf.Pow(glow, 2.2f);
            foreach (NozzleGlowModule.GlowMesh mesh in meshes)
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
    }
}
