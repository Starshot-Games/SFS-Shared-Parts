using Sirenix.OdinInspector;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Serialization;

namespace SFS.Parts.Modules
{
    [HideMonoScript, ExecuteInEditMode]
    public abstract class BaseMesh : MonoBehaviour
    {
        [FormerlySerializedAs("sortingLayerOffset")] public int renderQueueOffset = 0;

        // Generates mesh
        [Button] public abstract void GenerateMesh();

        // BaseDepth units -> global depth (per sorting layer)
        public const float DepthScale = 0.02f;

        static int baseDepthID = Shader.PropertyToID("_BaseDepth");
        static int depthMultiplierID = Shader.PropertyToID("_DepthMultiplier");

        // Caches
        MeshRenderer meshRendererCache;
        MeshFilter meshFilterCache;
        Material[] appliedMaterials;
        Material appliedMaterial;
        MaterialPropertyBlock propertyBlock;

        // Last applied submesh data, so property blocks can be refreshed without regenerating the mesh
        PartTex[] appliedTextures = new PartTex[0];
        float appliedBaseDepth, appliedDepthMultiplier;

        MeshRenderer GetRenderer() => meshRendererCache != null? meshRendererCache : meshRendererCache = this.GetOrAddComponent<MeshRenderer>();
        MeshFilter GetFilter() => meshFilterCache != null? meshFilterCache : meshFilterCache = this.GetOrAddComponent<MeshFilter>();

        // Meshes with identical data are shared between renderers in play mode
        static readonly Dictionary<Hash128, CachedMesh> meshCache = new Dictionary<Hash128, CachedMesh>();
        class CachedMesh { public Mesh mesh; public int users; }
        Hash128 cacheKey;
        bool usesCachedMesh;

        // Creates a mesh based on the specified values
        protected void ApplyMeshData(List<Vector3> vertices, int[] indices, List<Vector3>[] UVs, List<Color> colors, List<Vector3> shading, List<Vector3> depths, float baseDepth, float depthMultiplier, List<PartTex> textures, MeshTopology topology)
        {
            // Refs
            Renderer meshRenderer = GetRenderer();

            // Depth
            if (RenderSortingManager.main != null)
            {
                baseDepth = RenderSortingManager.main.GetGlobalDepth(0.5f, sortingLayer) + baseDepth * DepthScale * 1f / Mathf.Max(RenderSortingManager.main.layers.Count, 1);
                depthMultiplier = depthMultiplier * DepthScale * 1f / Mathf.Max(RenderSortingManager.main.layers.Count, 1);
            }
            else
            {
                baseDepth = 0.5f + baseDepth * DepthScale;
                depthMultiplier *= DepthScale;
            }
            appliedBaseDepth = baseDepth;
            appliedDepthMultiplier = depthMultiplier;

            // Submeshes, one per texture
            List<List<int>> submeshIndices = new List<List<int>>();
            List<PartTex> submeshTextures = new List<PartTex>();
            if (textures.Count > 1)
            {
                Dictionary<PartTex, List<int>> submeshes = new Dictionary<PartTex, List<int>>();
                for (int i = 0; i < textures.Count; i++)
                {
                    if (!submeshes.TryGetValue(textures[i], out List<int> list))
                    {
                        submeshes.Add(textures[i], list = new List<int>());
                        submeshIndices.Add(list);
                        submeshTextures.Add(textures[i]);
                    }

                    list.Add(indices[i * 4]);
                    list.Add(indices[i * 4 + 1]);
                    list.Add(indices[i * 4 + 2]);
                    list.Add(indices[i * 4 + 3]);
                }
            }
            if (submeshIndices.Count <= 1)
            {
                submeshIndices.Clear();
                submeshIndices.Add(new List<int>(indices));
                submeshTextures.Clear();
                if (textures.Count > 0)
                    submeshTextures.Add(textures[0]);
            }

            // Mesh
            Mesh mesh = GetMesh(GetMeshKey(vertices, UVs, colors, shading, depths, submeshIndices, topology), out bool needsData);
            if (needsData)
            {
                mesh.SetVertices(vertices);
                for (int channel_Index = 0; channel_Index < UVs.Length; channel_Index++)
                    mesh.SetUVs(channel_Index, UVs[channel_Index]);
                mesh.SetColors(colors);
                mesh.SetUVs(5, shading);
                mesh.SetUVs(3, depths);

                mesh.subMeshCount = submeshIndices.Count;
                for (int i = 0; i < submeshIndices.Count; i++)
                    mesh.SetIndices(submeshIndices[i], topology, i);

                mesh.RecalculateBounds();
            }

            // Materials, property blocks
            SetMaterials(submeshIndices.Count);
            appliedTextures = submeshTextures.ToArray();
            for (int i = 0; i < appliedTextures.Length; i++)
                ApplyPropertyBlock(meshRenderer, appliedTextures[i], i);

            void SetMaterials(int count)
            {
                Material material = null;
                bool engineGlow = this is PipeMesh pipeMesh && pipeMesh.engineGlowMaterial;
                if (Application.isPlaying)
                {
                    int renderQueue = RenderSortingManager.main.GetRenderQueue(sortingLayer) + renderQueueOffset;
                    material = engineGlow? RenderSortingManager.main.GetEngineGlowMaterial(renderQueue) : RenderSortingManager.main.GetPartMaterial(renderQueue);
                }
                #if UNITY_EDITOR
                else
                    material = (Material)UnityEditor.AssetDatabase.LoadAssetAtPath(engineGlow? "Assets/Materials/Engine Glow.mat" : "Assets/Materials/Part 1.mat", typeof(Material));
                #endif

                // Assigning sharedMaterials rebuilds the renderer's material state, so it's only done when it would actually change
                if (appliedMaterials != null && appliedMaterials.Length == count && appliedMaterial == material)
                    return;

                appliedMaterials = new Material[count];
                appliedMaterial = material;

                for (int i = 0; i < count; i++)
                    appliedMaterials[i] = material;

                meshRenderer.sharedMaterials = appliedMaterials;
            }
        }
        static Hash128 GetMeshKey(List<Vector3> vertices, List<Vector3>[] UVs, List<Color> colors, List<Vector3> shading, List<Vector3> depths, List<List<int>> submeshIndices, MeshTopology topology)
        {
            Hash128 hash = new Hash128();
            hash.Append((int)topology);
            Add(vertices);
            hash.Append(UVs.Length);
            foreach (List<Vector3> uv in UVs)
                Add(uv);
            Add(colors);
            Add(shading);
            Add(depths);
            hash.Append(submeshIndices.Count);
            foreach (List<int> submesh in submeshIndices)
                Add(submesh);
            return hash;

            void Add<T>(List<T> list) where T : unmanaged
            {
                hash.Append(list.Count);
                hash.Append(list);
            }
        }
        void ApplyPropertyBlock(Renderer meshRenderer, PartTex T, int index)
        {
            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            propertyBlock.Clear();

            if (meshRenderer.HasPropertyBlock())
                meshRenderer.GetPropertyBlock(propertyBlock, index);

            // Tex
            propertyBlock.SetTexture(PartTex.ColorTexture, T.color);
            propertyBlock.SetTexture(PartTex.ShapeTexture, T.shape);
            propertyBlock.SetTexture(PartTex.ShadowTexture, T.shadow);

            // Depth
            propertyBlock.SetFloat(baseDepthID, appliedBaseDepth);
            propertyBlock.SetFloat(depthMultiplierID, appliedDepthMultiplier);

            SetCustomProperties(propertyBlock);

            meshRenderer.SetPropertyBlock(propertyBlock, index);
        }

        // Extra per renderer properties (skin colors), applied with every property block
        protected virtual void SetCustomProperties(MaterialPropertyBlock block) { }

        // Re-applies property blocks of the current mesh, for changes that don't affect geometry
        protected void RefreshPropertyBlocks()
        {
            Renderer meshRenderer = GetRenderer();
            for (int i = 0; i < appliedTextures.Length; i++)
                ApplyPropertyBlock(meshRenderer, appliedTextures[i], i);
        }

        // Uses the cached mesh for this data if there is one, otherwise returns a mesh that needs the data set
        Mesh GetMesh(Hash128 key, out bool needsData)
        {
            MeshFilter meshFilter = GetFilter();
            needsData = true;

            if (!Application.isPlaying)
            {
                if (usesCachedMesh)
                    DestroyMesh(ReleaseMesh());
                return meshFilter.sharedMesh = meshReference = new Mesh { name = "BaseMesh" };
            }

            Mesh unused = null;
            if (meshCache.TryGetValue(key, out CachedMesh cached) && cached.mesh != null)
            {
                cached.users++;
                unused = ReleaseMesh();
                needsData = false;
            }
            else
            {
                Mesh mesh = ReleaseMesh();
                if (mesh != null)
                    mesh.Clear();
                else
                    mesh = new Mesh { name = "BaseMesh" };

                cached = meshCache[key] = new CachedMesh { mesh = mesh, users = 1 };
            }

            cacheKey = key;
            usesCachedMesh = true;
            meshFilter.sharedMesh = meshReference = cached.mesh;
            DestroyMesh(unused);
            return cached.mesh;
        }

        // Stops using the current mesh, returns it if nothing else uses it
        Mesh ReleaseMesh()
        {
            Mesh mesh = meshReference;
            meshReference = null;

            if (usesCachedMesh)
            {
                usesCachedMesh = false;
                if (meshCache.TryGetValue(cacheKey, out CachedMesh cached) && cached.mesh == mesh)
                {
                    if (--cached.users > 0)
                        return null;
                    meshCache.Remove(cacheKey);
                }
            }

            return mesh;
        }

        // Gives this renderer its own copy of a shared mesh, for per part vertex data like burn marks
        public Mesh GetUniqueMesh()
        {
            if (usesCachedMesh)
            {
                Mesh shared = meshReference;
                Mesh own = ReleaseMesh();
                GetFilter().sharedMesh = meshReference = own != null? own : Instantiate(shared);
            }

            return GetFilter().sharedMesh;
        }

        // Changes depth layer and re-generates mesh
        string sortingLayer;
        public void SetSortingLayer(string sortingLayer)
        {
            if (this.sortingLayer == sortingLayer)
                return;
            
            this.sortingLayer = sortingLayer;
            GenerateMesh();
        }

        // Clears memory
        Mesh meshReference;

        void OnDestroy() => DestroyMesh(ReleaseMesh());

        static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
                return;
#if UNITY_EDITOR
            DestroyImmediate(mesh);
#else
            Destroy(mesh);
#endif
        }

        // Auto regenerate mesh
        #if UNITY_EDITOR
        protected void OnValidate() => hasGeneratedMeshForEditing = true;
        bool hasGeneratedMeshForEditing;
        void Update()
        {
            if (!Application.isPlaying && hasGeneratedMeshForEditing)
            {
                hasGeneratedMeshForEditing = false;
                GenerateMesh();
            }
        }
        #endif
    }
    
    public struct PartTex
    {
        public Texture2D color, shape, shadow;
        
        public static readonly int ColorTexture = Shader.PropertyToID("_ColorTexture");
        public static readonly int ShapeTexture = Shader.PropertyToID("_ShapeTexture");
        public static readonly int ShadowTexture = Shader.PropertyToID("_ShadowTexture");
    }
}