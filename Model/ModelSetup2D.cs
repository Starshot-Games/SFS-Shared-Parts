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
        static readonly int FlipSigns = Shader.PropertyToID("_FlipSigns");

        // Degrees the R = 1 parts' diffuse flip is rotated by, relative to where the shadow flips.
        const float RedDiffuseFlipRotation = 70;

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
            Vector2 redDiffuseLightDirection = RotateDegrees(lightDirection, RedDiffuseFlipRotation);

            foreach (MeshRenderer r in meshRenderers)
            {
                if (r == null)
                {
                    Debug.LogWarning("MeshRenderer is null");
                    continue;
                }
                
                Vector2 flip = GetFlipSigns(r.transform, lightDirection);
                float flipRedDiffuse = GetFlipSigns(r.transform, redDiffuseLightDirection).x;

                MaterialPropertyBlock propertyBlock = new();
                ApplyDepth(propertyBlock);
                propertyBlock.SetVector(FlipSigns, new Vector4(flip.x, flip.y, flipRedDiffuse, 0));
                r.SetPropertyBlock(propertyBlock);
            }
        }
        
        static Vector2 GetFlipSigns(Transform t, Vector2 lightDirection)
        {
            Matrix4x4 m = t.localToWorldMatrix;
            Vector2 light = lightDirection.normalized;
            float sX = Vector2.Dot(new Vector2(m.m00, m.m10).normalized, light);
            float sY = Vector2.Dot(new Vector2(m.m01, m.m11).normalized, light);

            return new Vector2(sX > 0.02f ? -1 : 1, sY < -0.02f ? -1 : 1);
        }

        // Rotates a vector counter-clockwise
        static Vector2 RotateDegrees(Vector2 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);

            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }

        // Writes the depth values ("Part 2d Model" shader convention) into the property block.
        void ApplyDepth(MaterialPropertyBlock propertyBlock)
        {
            propertyBlock.SetFloat(DepthStart, GetGlobalDepth(0.5f, sortingLayer));
            propertyBlock.SetFloat(DepthM, (GetGlobalDepth(1, sortingLayer) - GetGlobalDepth(0, sortingLayer)) * 0.04f);
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