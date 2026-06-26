/*using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class ModelSetup3D : MonoBehaviour, I_InitializePartModule
    {
        static readonly int DepthStart = Shader.PropertyToID("_DepthStart");
        static readonly int DepthM = Shader.PropertyToID("_DepthM");
        static readonly int LightNormal = Shader.PropertyToID("_LightNormal");


        // Refs
        public MeshRenderer[] meshRenderers;
        //public Texture2D colorTex, normalTex;
        //public float smoothness;
        //public int renderQueueOffset;
        //public bool useNormals;
        //public bool dontSetMaterial;


        [Button]
        public void GetRenderers() => meshRenderers = GetComponentsInChildren<MeshRenderer>();
        void Reset() => GetRenderers();


        // Changes depth layer and re-generates mesh
        string sortingLayer;
        public void SetSortingLayer(string sortingLayer)
        {
            this.sortingLayer = sortingLayer;
            SetMesh();
        }


        [Button]
        public void SetMesh()
        {
            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                /*if (meshRenderer == null || colorTex == null || normalTex == null)
                {
                    Debug.LogWarning(new Exception("Something null at " + name));
                    continue;
                }*/

                //if (!dontSetMaterial)
                //meshRenderer.sharedMaterial = GetMaterial();

                // Property block
                /*MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

                // Depth
                float depthStart = GetGlobalDepth(0.5f, sortingLayer); // + depthOffset * 0.05f);
                float depthM = (GetGlobalDepth(1, sortingLayer) - GetGlobalDepth(0, sortingLayer)) * 0.05f;
                propertyBlock.SetFloat(DepthStart, depthStart);
                propertyBlock.SetFloat(DepthM, depthM);

                // Texture
                //propertyBlock.SetTexture(ColorTex, colorTex);
                //propertyBlock.SetTexture(NormalTex, normalTex);

                // Material property
                //propertyBlock.SetFloat(Smoothness, smoothness);

                // Light
                Vector3 lightLocal = GetLightDirection(meshRenderer);
                propertyBlock.SetVector(LightNormal, lightLocal);

                //Vector3 viewLocal = meshRenderer.transform.InverseTransformVector(Vector3.forward).normalized;
                //propertyBlock.SetVector(ViewNormal, viewLocal);

                meshRenderer.SetPropertyBlock(propertyBlock);
            }
        }

        // Some depth utility
        /*float depthOffset = 0;
        public void SetDepthOffset(float depthOffset)
        {
            this.depthOffset = depthOffset;
            SetMesh();
        }*/

        // Utility
        /*Material GetMaterial()
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying)
                return (Material)UnityEditor.AssetDatabase.LoadAssetAtPath(useNormals? "Assets/Editor/Modeling/Part 2 normals.mat" : "Assets/Editor/Modeling/Part 2 texture.mat", typeof(Material));
            #endif

            int renderQueue = RenderSortingManager.main.GetRenderQueue(sortingLayer) + renderQueueOffset;
            return RenderSortingManager.main.GetPartModelMaterial(renderQueue, useNormals);
        }*/

        /*public static float GetGlobalDepth(float depth, string sortingLayer)
        {
            return RenderSortingManager.main != null ? RenderSortingManager.main.GetGlobalDepth(depth, sortingLayer) : depth;
        }

        Vector3 GetLightDirection(MeshRenderer meshRenderer)
        {
            /*Vector3 lightGlobal = new Vector3( useNormals? 0.2f : -0.2f, useNormals? -0.4f : 0.4f, 1).normalized;
            
            if (GameManager.main != null)
                return transform.root.GetChild(0).TransformDirection(lightGlobal);
                
            return lightGlobal;*/

            /*return new Vector3(-0.4f, 0.25f, -1).normalized;
        }


        int I_InitializePartModule.Priority => 0;
        void I_InitializePartModule.Initialize() => SetMesh();
    }
}
*/