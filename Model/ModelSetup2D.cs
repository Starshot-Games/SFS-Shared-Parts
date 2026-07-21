using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using SFS.World;
using UnityEngine;

// only reference non-generated meshes, 

namespace SFS.Parts.Modules
{
    public class ModelSetup2D : MonoBehaviour, I_InitializePartModule
    {
        static readonly int DepthStart = Shader.PropertyToID("_DepthStart");
        static readonly int DepthM = Shader.PropertyToID("_DepthM");
        static readonly int LightDirection = Shader.PropertyToID("_LightDirection");

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
            Vector2 lightDirection = GetLightDirection(transform);

            foreach (MeshRenderer r in meshRenderers)
            {
                if (r == null)
                {
                    Debug.LogWarning("MeshRenderer is null");
                    continue;
                }

                MaterialPropertyBlock propertyBlock = new();
                ApplyDepth(propertyBlock);
                propertyBlock.SetVector(LightDirection, lightDirection); // Shader decides flip per-fragment from the UV axes vs this direction
                r.SetPropertyBlock(propertyBlock);
            }
        }

        // Writes the depth values ("Part 2d Model" shader convention) into the property block.
        void ApplyDepth(MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetFloat(DepthStart, GetGlobalDepth(0.5f, sortingLayer));
            propertyBlock.SetFloat(DepthM, (GetGlobalDepth(1, sortingLayer) - GetGlobalDepth(0, sortingLayer)) * 0.05f);
        }
        
        public static Vector2 GetLightDirection(Transform t)
        {
            Vector2 a = new Vector2(-1, 1);

            if (GameManager.main != null && t.root.childCount > 0 && t.root.GetChild(0).name == "Parts Holder")
                return t.root.GetChild(0).TransformDirection(a);

            return a;
        }

        public static float GetGlobalDepth(float depth, string sortingLayer)
            => RenderSortingManager.main != null ? RenderSortingManager.main.GetGlobalDepth(depth, sortingLayer) : depth;
        
        int I_InitializePartModule.Priority => 0;
        void I_InitializePartModule.Initialize() => SetMesh();
    }
}