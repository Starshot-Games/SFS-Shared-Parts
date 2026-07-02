using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

// only reference non-generated meshes, 

namespace SFS.Parts.Modules
{
    public class ModelSetup2D : MonoBehaviour, I_InitializePartModule
    {
        static readonly int DepthStart = Shader.PropertyToID("_DepthStart");
        static readonly int DepthM = Shader.PropertyToID("_DepthM");

        public MeshRenderer[] meshRenderers;
        
        
        [Button(ButtonSizes.Large)]
        public void GetRenderers()
        {
            // Flame meshes are driven by FlameMeshModule (generated effects) - exclude those.
            HashSet<MeshRenderer> flameRenderers = new();
            foreach (FlameMeshModule flame in GetComponentsInChildren<FlameMeshModule>(true))
                foreach (FlameMeshModule.MeshRef meshRef in flame.meshRenderers)
                    if (meshRef?.meshRenderer != null)
                        flameRenderers.Add(meshRef.meshRenderer);

            meshRenderers = GetComponentsInChildren<MeshRenderer>(true).Where(r => !flameRenderers.Contains(r)).ToArray();
        }
        void Reset() => GetRenderers();
        
        // Changes depth layer and re-generates mesh
        string sortingLayer;
        public void SetSortingLayer(string sortingLayer)
        {
            this.sortingLayer = sortingLayer;
            SetMesh();
        }
        
        public void SetMesh()
        {
            foreach (MeshRenderer r in meshRenderers)
            {
                if (r == null)
                {
                    Debug.LogWarning("MeshRenderer is null");
                    continue;
                }

                MaterialPropertyBlock propertyBlock = new();
                ApplyDepth(propertyBlock);
                r.SetPropertyBlock(propertyBlock);
            }
        }

        // Writes the depth values into an externally-owned property block. Modules that set their own
        // blocks on these renderers (e.g. NozzleGlow) must include depth too - their block replaces
        // this one on the material, so leaving depth out would wipe it.
        public void ApplyDepth(MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetFloat(DepthStart, GetGlobalDepth(0.5f, sortingLayer));
            propertyBlock.SetFloat(DepthM, (GetGlobalDepth(1, sortingLayer) - GetGlobalDepth(0, sortingLayer)) * 0.05f);
        }
        
        public static float GetGlobalDepth(float depth, string sortingLayer)
            => RenderSortingManager.main != null ? RenderSortingManager.main.GetGlobalDepth(depth, sortingLayer) : depth;
        
        int I_InitializePartModule.Priority => 0;
        void I_InitializePartModule.Initialize() => SetMesh();
    }
}