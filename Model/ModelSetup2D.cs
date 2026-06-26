using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class ModelSetup2D : MonoBehaviour, I_InitializePartModule
    {
        static readonly int DepthStart = Shader.PropertyToID("_DepthStart");
        static readonly int DepthM = Shader.PropertyToID("_DepthM");

        public MeshRenderer[] meshRenderers;


        [Button(ButtonSizes.Large)]
        public void GetRenderers() => meshRenderers = GetComponentsInChildren<MeshRenderer>();
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
                
                // Depth
                float depthStart = GetGlobalDepth(0.5f, sortingLayer);
                float depthM = (GetGlobalDepth(1, sortingLayer) - GetGlobalDepth(0, sortingLayer)) * 0.05f;
                propertyBlock.SetFloat(DepthStart, depthStart);
                propertyBlock.SetFloat(DepthM, depthM);
                
                r.SetPropertyBlock(propertyBlock);
            }
        }
        
        public static float GetGlobalDepth(float depth, string sortingLayer)
        {
            return RenderSortingManager.main != null ? RenderSortingManager.main.GetGlobalDepth(depth, sortingLayer) : depth;
        }
        
        int I_InitializePartModule.Priority => 0;
        void I_InitializePartModule.Initialize() => SetMesh();
    }
}