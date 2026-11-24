using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SFS.Parts.Modules
{
    // Puts every engine glow's haze over the scene at once, so that where glows overlap their light adds up only as far as their
    // overlap lets it.
    public static class FlameGlowCompositor
    {
        const int Slots = 4; // _GlowLight0.. in the composite shader
        const int AccumulatePass = 1; // of the FlameGlow shader

        static readonly int[] LightTextures = { Shader.PropertyToID("_GlowLight0"), Shader.PropertyToID("_GlowLight1"), Shader.PropertyToID("_GlowLight2"), Shader.PropertyToID("_GlowLight3") };
        static readonly int[] TintTextures = { Shader.PropertyToID("_GlowTint0"), Shader.PropertyToID("_GlowTint1"), Shader.PropertyToID("_GlowTint2"), Shader.PropertyToID("_GlowTint3") };
        static readonly int
            Slot = Shader.PropertyToID("_Slot"),
            Haze = Shader.PropertyToID("_Haze"),
            Light = Shader.PropertyToID("_Light"),
            SkyHold = Shader.PropertyToID("_SkyHold"),
            Highlights = Shader.PropertyToID("_Highlights"),
            GlowSlot = Shader.PropertyToID("glowSlot");
        static readonly int[] Looks = { Haze, Light, SkyHold, Highlights };

        // The glows that look alike, in one sorting slot and scene, accumulated together and composited by one quad
        class Group
        {
            public Scene scene;
            public Material[] materials;
            public int layer, sortingLayer, sortingOrder;
            public MeshRenderer quad;
            public Material[] composites;
            public readonly List<FlameGlowModule> glows = new();

            public bool Matches(FlameGlowModule glow, Material[] glowMaterials)
            {
                MeshRenderer renderer = glow.glow;
                if (renderer.gameObject.scene != scene || renderer.sortingLayerID != sortingLayer || renderer.sortingOrder != sortingOrder || renderer.gameObject.layer != layer || glowMaterials.Length != materials.Length)
                    return false;
                for (int i = 0; i < materials.Length; i++)
                    if (!Alike(glowMaterials[i], materials[i]))
                        return false;
                return true;
            }
        }

        // Parts can each carry their own copy of a glow material, so it is what a material draws that groups glows, not which one it is
        static bool Alike(Material a, Material b)
        {
            if (a == b)
                return true;
            if (a == null || b == null || a.shader != b.shader || a.renderQueue != b.renderQueue)
                return false;
            foreach (int property in Looks)
                if (a.HasProperty(property) && b.HasProperty(property) && a.GetFloat(property) != b.GetFloat(property))
                    return false;
            return true;
        }

        static readonly List<FlameGlowModule> glows = new();
        static readonly HashSet<FlameGlowModule> registered = new();
        static readonly List<Group> groups = new();
        static readonly Dictionary<Camera, CommandBuffer> buffers = new();
        static readonly List<Camera> gone = new();
        static readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
        static Shader compositeShader;
        static Mesh quadMesh;
        static bool? available;
        static bool hooked;

        public static bool Available
        {
            get
            {
                if (available == null)
                {
                    compositeShader = UnityEngine.Resources.Load<Shader>("Effects/FlameGlowComposite");
                    available = compositeShader != null && compositeShader.isSupported && SystemInfo.supportedRenderTargetCount >= 2
                        && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
                }
                return available.Value;
            }
        }

        public static void Add(FlameGlowModule glow)
        {
            if (!registered.Add(glow))
                return;
            glows.Add(glow);

            if (!hooked)
            {
                Camera.onPreCull += Prepare; // before culling, so a quad enabled here is drawn this frame
                hooked = true;
            }
        }

        public static void Remove(FlameGlowModule glow)
        {
            if (registered.Remove(glow))
                glows.Remove(glow);
        }

        #if UNITY_EDITOR
        // A script reload forgets the quads and buffers, which would then stay on screen with nothing left to update them
        [UnityEditor.InitializeOnLoadMethod]
        static void ReleaseBeforeReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Release;

        static void Release()
        {
            foreach (KeyValuePair<Camera, CommandBuffer> attached in buffers)
                if (attached.Key != null)
                    attached.Key.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, attached.Value);
            buffers.Clear();

            foreach (Group group in groups)
            {
                if (group.quad != null)
                    Object.DestroyImmediate(group.quad.gameObject);
                if (group.composites != null)
                    foreach (Material composite in group.composites)
                        if (composite != null)
                            Object.DestroyImmediate(composite);
            }
            groups.Clear();

            if (quadMesh != null)
                Object.DestroyImmediate(quadMesh);
        }
        #endif

        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null)
                    return quadMesh;

                // Its corners are clip-space, and its bounds reach far enough that it's never culled
                quadMesh = new Mesh { name = "Flame Glow Composite", hideFlags = HideFlags.HideAndDontSave };
                quadMesh.SetVertices(new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) });
                quadMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, false);
                quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
                return quadMesh;
            }
        }

        // Whether a camera draws the glow at all: one that sees its layer, in its scene if the camera renders a scene of its own
        static bool Draws(Camera camera, FlameGlowModule glow)
        {
            MeshRenderer renderer = glow.glow;
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
                return false;
            if ((camera.cullingMask & (1 << renderer.gameObject.layer)) == 0)
                return false;
            return camera.scene.IsValid() ? renderer.gameObject.scene == camera.scene : !IsPreviewScene(renderer.gameObject.scene);
        }

        static bool IsPreviewScene(Scene scene)
        {
            #if UNITY_EDITOR
            return UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene);
            #else
            return false;
            #endif
        }

        static Group GetGroup(FlameGlowModule glow, Material[] materials)
        {
            foreach (Group group in groups)
                if (group.Matches(glow, materials))
                    return group;

            MeshRenderer renderer = glow.glow;
            Group created = new()
            {
                scene = renderer.gameObject.scene,
                materials = materials,
                layer = renderer.gameObject.layer,
                sortingLayer = renderer.sortingLayerID,
                sortingOrder = renderer.sortingOrder,
            };
            groups.Add(created);
            return created;
        }

        // The quad that composites a group, with one material for each of the group's
        static void SetUpQuad(Group group, int slot)
        {
            if (group.quad == null)
            {
                GameObject quad = new("Flame Glow Composite");
                if (group.scene.IsValid() && quad.scene != group.scene)
                    SceneManager.MoveGameObjectToScene(quad, group.scene);
                quad.hideFlags = HideFlags.HideAndDontSave;
                quad.layer = group.layer;
                quad.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
                group.quad = quad.AddComponent<MeshRenderer>();
                group.quad.shadowCastingMode = ShadowCastingMode.Off;
                group.quad.receiveShadows = false;
                group.quad.lightProbeUsage = LightProbeUsage.Off;
                group.quad.reflectionProbeUsage = ReflectionProbeUsage.Off;
                group.quad.sortingLayerID = group.sortingLayer;
                group.quad.sortingOrder = group.sortingOrder;

                group.composites = new Material[group.materials.Length];
                for (int i = 0; i < group.materials.Length; i++)
                    group.composites[i] = new Material(compositeShader) { name = (group.materials[i] != null ? group.materials[i].name : "None") + " (Composite)", hideFlags = HideFlags.HideAndDontSave };
                group.quad.sharedMaterials = group.composites;
            }

            // Kept up with its glow's material, so edits to it show; just ahead of that material, so its highlights go over the haze
            for (int i = 0; i < group.materials.Length; i++)
            {
                Material material = group.materials[i];
                Material composite = group.composites[i];
                float haze = material != null && material.HasProperty(Haze) ? material.GetFloat(Haze) : 0;
                float light = material != null && material.HasProperty(Light) ? material.GetFloat(Light) : 1;
                bool highlights = light > 0 && material != null && (!material.HasProperty(Highlights) || material.GetFloat(Highlights) > 0);
                composite.SetFloat(Haze, haze);
                composite.SetFloat(Light, light);
                composite.SetFloat(SkyHold, material != null && material.HasProperty(SkyHold) ? material.GetFloat(SkyHold) : 0);
                composite.SetFloat(Slot, slot);
                composite.renderQueue = material != null ? material.renderQueue - (highlights ? 1 : 0) : 3000;
            }
            group.quad.enabled = true;
        }

        // Refills the camera's buffer with the glows it draws, where they are now
        static void Prepare(Camera camera)
        {
            for (int i = glows.Count - 1; i >= 0; i--)
                if (glows[i] == null || glows[i].glow == null)
                {
                    registered.Remove(glows[i]);
                    glows.RemoveAt(i);
                }

            gone.Clear();
            foreach (Camera attached in buffers.Keys)
                if (attached == null)
                    gone.Add(attached);
            foreach (Camera attached in gone)
                buffers.Remove(attached);

            for (int i = groups.Count - 1; i >= 0; i--)
            {
                Group group = groups[i];
                group.glows.Clear();
                if (!group.scene.IsValid() || !group.scene.isLoaded)
                {
                    if (group.quad != null)
                        Object.DestroyImmediate(group.quad.gameObject);
                    groups.RemoveAt(i);
                }
            }

            bool draws = Available && camera.cameraType is CameraType.Game or CameraType.SceneView or CameraType.Preview;
            if (draws)
                foreach (FlameGlowModule glow in glows)
                    if (Draws(camera, glow))
                    {
                        Material[] materials = glow.glow.sharedMaterials;
                        if (materials.Length > 0 && materials[0] != null)
                            GetGroup(glow, materials).glows.Add(glow);
                    }

            if (!buffers.TryGetValue(camera, out CommandBuffer buffer))
            {
                if (!draws)
                    return;
                buffer = new CommandBuffer { name = "Flame Glows" };
                camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, buffer);
                buffers.Add(camera, buffer);
            }

            buffer.Clear();
            int slot = 0;
            foreach (Group group in groups)
            {
                if (group.glows.Count == 0 || slot >= Slots)
                {
                    if (group.quad != null)
                        group.quad.enabled = false;
                    continue;
                }
                group.materials = group.glows[0].glow.sharedMaterials;

                // At half the screen's resolution: the haze is soft, and this is where all of the glows' light is worked out
                buffer.GetTemporaryRT(LightTextures[slot], -2, -2, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                buffer.GetTemporaryRT(TintTextures[slot], -2, -2, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf);
                targets[0] = LightTextures[slot];
                targets[1] = TintTextures[slot];
                buffer.SetRenderTarget(targets, targets[0]);
                buffer.ClearRenderTarget(false, true, Color.clear);
                foreach (FlameGlowModule glow in group.glows)
                    if (glow.Mesh != null && glow.PropertyBlock != null)
                    {
                        glow.PropertyBlock.SetFloat(GlowSlot, slot);
                        glow.glow.SetPropertyBlock(glow.PropertyBlock);
                        buffer.DrawMesh(glow.Mesh, glow.glow.localToWorldMatrix, group.materials[0], 0, AccumulatePass, glow.PropertyBlock);
                    }
                buffer.SetGlobalTexture(LightTextures[slot], LightTextures[slot]);
                buffer.SetGlobalTexture(TintTextures[slot], TintTextures[slot]);

                SetUpQuad(group, slot);
                slot++;
            }
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        }
    }
}
