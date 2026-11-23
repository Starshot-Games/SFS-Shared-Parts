using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SFS.Parts.Modules
{
    // Draws the flame plumes a camera sees in one pass rather than two. Each plume works out both of its looks at once, the alpha pass's
    // layer into one texture and the additive pass's light into another, and one quad then puts both over the scene in the plumes' place
    // in the draw order: every layer over what is behind them, then all the light. That is how Unity draws the two passes of flames tied
    // in depth, as a craft's plumes are. Their diamonds aren't: they sit a little nearer than their plume, and are drawn as before.
    public static class FlameCompositor
    {
        const string AccumulatePass = "FLAMEACCUMULATE"; // of the flame shader

        const int KeepUnused = 120; // frames a camera keeps its textures without any plume to draw

        static readonly int Layer = Shader.PropertyToID("_FlameLayer"), Light = Shader.PropertyToID("_FlameLight");

        // A camera's own two textures. Not temporary ones: those go back to the pool once the command buffer is done, long before the
        // quad reads them, and the next buffer to ask for the same kind is handed them - the smoke's, which then draws into them
        class Targets
        {
            public RenderTexture layer, light;
            public int lastUsed;

            public bool Fits(Camera camera, RenderTextureFormat format) => layer != null && layer.width == camera.pixelWidth && layer.height == camera.pixelHeight && layer.format == format;

            public void Release()
            {
                if (layer != null)
                    Object.DestroyImmediate(layer);
                if (light != null)
                    Object.DestroyImmediate(light);
                layer = light = null;
            }
        }

        // Where in the draw order the plumes go: the quad takes their place, and only plumes that share it can be put in at once
        struct Place
        {
            public Scene scene;
            public int queue, sortingLayer, sortingOrder, layer;
            public float z;

            public Place(MeshRenderer renderer)
            {
                scene = renderer.gameObject.scene;
                queue = renderer.sharedMaterial.renderQueue;
                sortingLayer = renderer.sortingLayerID;
                sortingOrder = renderer.sortingOrder;
                layer = renderer.gameObject.layer;
                z = renderer.transform.position.z;
            }

            public bool Matches(in Place other) => scene == other.scene && queue == other.queue && sortingLayer == other.sortingLayer && sortingOrder == other.sortingOrder && layer == other.layer && z == other.z;
        }

        static readonly List<FlameMeshModule> flames = new();
        static readonly HashSet<FlameMeshModule> registered = new();
        static readonly List<FlameMeshModule.MeshRef> drawn = new();
        static readonly Dictionary<Camera, CommandBuffer> buffers = new();
        static readonly Dictionary<Camera, Targets> textures = new();
        static readonly Dictionary<Scene, MeshRenderer> quads = new();
        static readonly List<Camera> goneCameras = new();
        static readonly List<Scene> goneScenes = new();
        static readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
        static readonly Plane[] frustum = new Plane[6];
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
                    compositeShader = UnityEngine.Resources.Load<Shader>("Effects/FlameComposite");
                    available = compositeShader != null && compositeShader.isSupported && SystemInfo.supportedRenderTargetCount >= 2
                        && SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBHalf);
                }
                return available.Value;
            }
        }

        public static void Add(FlameMeshModule flame)
        {
            if (!registered.Add(flame))
                return;
            flames.Add(flame);

            if (!hooked)
            {
                Camera.onPreCull += Prepare; // before culling, so the plumes it takes over are left out of it
                hooked = true;
            }
        }

        public static void Remove(FlameMeshModule flame)
        {
            if (!registered.Remove(flame))
                return;
            flames.Remove(flame);

            foreach (FlameMeshModule.MeshRef plume in flame.meshRenderers)
                if (plume.meshRenderer != null)
                    plume.meshRenderer.forceRenderingOff = false;
        }

        static Mesh QuadMesh
        {
            get
            {
                if (quadMesh != null)
                    return quadMesh;

                // Its corners are clip-space, and its bounds reach far enough that it's never culled
                quadMesh = new Mesh { name = "Flame Composite", hideFlags = HideFlags.HideAndDontSave };
                quadMesh.SetVertices(new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) });
                quadMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, false);
                quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);
                return quadMesh;
            }
        }

        // Whether a camera sees a plume at all: one that sees its layer, in its scene if the camera renders a scene of its own
        static bool Sees(Camera camera, MeshRenderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMaterial == null)
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

        // The quad that puts a scene's plumes in, sorted where they would have been
        static MeshRenderer GetQuad(in Place place)
        {
            if (!quads.TryGetValue(place.scene, out MeshRenderer quad) || quad == null)
            {
                GameObject gameObject = new("Flame Composite") { hideFlags = HideFlags.HideAndDontSave };
                if (place.scene.IsValid() && gameObject.scene != place.scene)
                    SceneManager.MoveGameObjectToScene(gameObject, place.scene);
                gameObject.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
                quad = gameObject.AddComponent<MeshRenderer>();
                quad.sharedMaterial = new Material(compositeShader) { name = "Flame Composite", hideFlags = HideFlags.HideAndDontSave };
                quad.shadowCastingMode = ShadowCastingMode.Off;
                quad.receiveShadows = false;
                quad.lightProbeUsage = LightProbeUsage.Off;
                quad.reflectionProbeUsage = ReflectionProbeUsage.Off;
                quads[place.scene] = quad;
            }

            quad.gameObject.layer = place.layer;
            quad.sortingLayerID = place.sortingLayer;
            quad.sortingOrder = place.sortingOrder;
            quad.sharedMaterial.renderQueue = place.queue;
            quad.transform.position = new Vector3(0, 0, place.z);
            return quad;
        }

        // Takes over the plumes this camera draws, and refills its buffer with them where they are now
        static void Prepare(Camera camera)
        {
            for (int i = flames.Count - 1; i >= 0; i--)
                if (flames[i] == null)
                {
                    registered.Remove(flames[i]);
                    flames.RemoveAt(i);
                }

            goneCameras.Clear();
            foreach (Camera attached in buffers.Keys)
                if (attached == null)
                    goneCameras.Add(attached);
            foreach (KeyValuePair<Camera, Targets> kept in textures)
                if (kept.Key == null || Time.frameCount - kept.Value.lastUsed > KeepUnused)
                    kept.Value.Release();
            foreach (Camera attached in goneCameras)
            {
                buffers.Remove(attached);
                textures.Remove(attached);
            }

            goneScenes.Clear();
            foreach (KeyValuePair<Scene, MeshRenderer> quad in quads)
            {
                if (quad.Key.IsValid() && quad.Key.isLoaded && quad.Value != null)
                {
                    quad.Value.enabled = false;
                    continue;
                }
                if (quad.Value != null)
                    Object.DestroyImmediate(quad.Value.gameObject);
                goneScenes.Add(quad.Key);
            }
            foreach (Scene scene in goneScenes)
                quads.Remove(scene);

            // Only plumes that share the first one's place in the draw order go in at once; any other is drawn as before
            bool draws = Available && camera.cameraType is CameraType.Game or CameraType.SceneView;
            Place place = default;
            bool placed = false;
            drawn.Clear();
            if (draws)
                GeometryUtility.CalculateFrustumPlanes(camera, frustum);

            foreach (FlameMeshModule flame in flames)
                foreach (FlameMeshModule.MeshRef plume in flame.meshRenderers)
                {
                    MeshRenderer renderer = plume.meshRenderer;
                    if (plume.machDiamondsMesh || renderer == null)
                        continue;

                    bool taken = false;
                    if (draws && plume.propertyBlock != null && Sees(camera, renderer) && renderer.sharedMaterial.FindPass(AccumulatePass) >= 0 && FlameMeshModule.GetMesh(plume) != null)
                    {
                        Place own = new(renderer);
                        if (!placed)
                        {
                            place = own;
                            placed = true;
                        }

                        taken = own.Matches(place);
                        if (taken && GeometryUtility.TestPlanesAABB(frustum, renderer.bounds))
                            drawn.Add(plume);
                    }
                    renderer.forceRenderingOff = taken;
                }

            if (!buffers.TryGetValue(camera, out CommandBuffer buffer))
            {
                if (drawn.Count == 0)
                    return;
                buffer = new CommandBuffer { name = "Flames" };
                camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, buffer);
                buffers.Add(camera, buffer);
            }

            buffer.Clear();
            if (drawn.Count == 0)
                return;

            // In the camera's own format, so each layer and its light clamp and round as they would have on the screen
            RenderTextureFormat format = camera.allowHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            if (!textures.TryGetValue(camera, out Targets owned))
                textures.Add(camera, owned = new Targets());
            if (!owned.Fits(camera, format))
            {
                owned.Release();
                owned.layer = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, format) { name = "Flame Layers", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                owned.light = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0, format) { name = "Flame Light", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            }
            owned.lastUsed = Time.frameCount;

            targets[0] = owned.layer;
            targets[1] = owned.light;
            buffer.SetRenderTarget(targets, targets[0]);
            buffer.ClearRenderTarget(false, true, Color.clear);
            foreach (FlameMeshModule.MeshRef plume in drawn)
            {
                Material material = plume.meshRenderer.sharedMaterial;
                buffer.DrawMesh(FlameMeshModule.GetMesh(plume), plume.meshRenderer.localToWorldMatrix, material, 0, material.FindPass(AccumulatePass), plume.propertyBlock);
            }
            buffer.SetGlobalTexture(Layer, owned.layer);
            buffer.SetGlobalTexture(Light, owned.light);
            buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);

            GetQuad(place).enabled = true;
        }
    }
}
