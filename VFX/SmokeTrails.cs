using System.Collections.Generic;
using System.Runtime.InteropServices;
using SFS.Platform;
using SFS.World;
using SFS.WorldBase;
using UnityEngine;
using UnityEngine.Rendering;

namespace SFS.Parts.Modules
{
    // Simulates and draws the smoke trails left in the air. Trails are kept in planet-centred coordinates and
    // only brought into the WorldView's local space when drawn, so they stay put however its origin moves.
    public class SmokeTrails : MonoBehaviour
    {
        static readonly int NoiseOrigin = Shader.PropertyToID("_NoiseOrigin");
        static readonly int NoiseTileProperty = Shader.PropertyToID("_NoiseTile");
        static readonly int PuffSize = Shader.PropertyToID("_PuffSize");
        static readonly int MovingSpeed = Shader.PropertyToID("_MovingSpeed");
        static readonly int Sun = Shader.PropertyToID("_Sun");
        static readonly int Accumulated = Shader.PropertyToID("_SmokeAccumulated");
        static readonly int Airborne = Shader.PropertyToID("_SmokeAirborne");
        static readonly int AirborneLayer = Shader.PropertyToID("_Airborne");
        static readonly int Resolution = Shader.PropertyToID("_SmokeResolution");
        static readonly int ScreenRect = Shader.PropertyToID("_SmokeRect");
        static readonly int EdgeBreakup = Shader.PropertyToID("_EdgeBreakup");
        const int AccumulatePass = 1; // of the trail shader; pass 0 draws straight onto the screen
        const int AccumulateSplitPass = 2; // and pass 2 into two textures, the smoke still in the air apart from what the ground has turned aside
        const int AirborneQueue = 2995; // the flames': smoke not yet down on the ground goes behind the launch pad (2996) and the parts with them
        const float GroundShowTime = 0.4f; // s smoke the ground has turned aside takes to come out in front of the pad
        const float AirSqueeze = 4; // octaves smoke slowing in the air may squeeze its puffs before they hand over to the still ones, over one more

        const float MinDepth = 0.002f; // segments fainter than this are left out
        const float EdgeReach = 1.45f; // of the radius, how far the shader's ragged edges reach at full edge breakup
        const float ReachMargin = 1.01f;
        const float ConeSlope = 0.8f;  // m the smoke can have spread out per m back from where it comes out, or more where its flame widens faster
        const double NoiseTile = 8192; // m across the tile of the shader's largest puffs; smaller ones halve it, so its fractions serve them all
        const float SunTurn = 25;      // ° the light comes from left of straight up

        // Hard limits, whatever is flying: past them the oldest smoke goes first
        const int MaxTrails = 256;
        const int MaxTotalPoints = 20000;
        const int MaxSegments = 16000; // drawn per frame, which also keeps the mesh within 16-bit indices

        // How many times coarser than the screen the smoke is accumulated at, before the composite spreads it back over it. 2 fills a quarter
        // of the pixels, most of a frame on the pad, but zoomed far out it thickens the soft top of the cloud, so it is off for now
        public static int downsample = 1;
        static readonly Dictionary<Material, SmokeTrails> renderers = new();

        public static SmokeTrail StartTrail(Material material, Planet planet, float side, float spreadPhase)
        {
            if (!renderers.TryGetValue(material, out SmokeTrails renderer) || renderer == null)
            {
                renderer = new GameObject("Smoke Trails").AddComponent<SmokeTrails>();
                renderer.Setup(material);
                renderers[material] = renderer;
            }

            if (renderer.trails.Count >= MaxTrails)
            {
                renderer.trails[0].Removed = true;
                renderer.trails.RemoveAt(0);
            }

            SmokeTrail trail = new(planet, side, spreadPhase);
            renderer.trails.Add(trail);
            return trail;
        }

        // The engine smoke graphics setting: with it off there's no smoke at all
        public static bool Enabled
        {
            get
            {
                if (PlatformManager.current == PlatformType.Mobile)
                    return VideoSettings.main == null || VideoSettings.main.settings == null || VideoSettings.main.settings.engineSmoke;
                return VideoSettingsPC.main == null || VideoSettingsPC.main.settings == null || VideoSettingsPC.main.settings.engineSmoke;
            }
        }

        public static void Clear()
        {
            foreach (SmokeTrails renderer in renderers.Values)
                if (renderer != null)
                    renderer.ClearTrails();
        }


        readonly List<SmokeTrail> trails = new();

        Mesh mesh;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock propertyBlock;
        Material material;
        Double2 anchor; // planet-centred position of this object's origin when the mesh was built

        // One vertex as the trail shader reads it, all in a single stream
        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public Vector3 position;
            public Color32 color;
            public Vector4 segment, shape, smoke, carried;
            public Vector2 ends;
            public Vector3 speed;
            public Vector2 cone, ground;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
            new(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
            new(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4),
            new(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 4),
            new(VertexAttribute.TexCoord4, VertexAttributeFormat.Float32, 2),
            new(VertexAttribute.TexCoord5, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord6, VertexAttributeFormat.Float32, 2),
            new(VertexAttribute.TexCoord7, VertexAttributeFormat.Float32, 2),
        };
        const MeshUpdateFlags UpdateFlags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds;

        Vertex[] vertices = new Vertex[1024];
        int vertexCount, vertexCapacity;
        bool indicesSet;
        Vector2 boundsMin, boundsMax;
        bool hasGround; // whether any smoke drawn has come out in front of the launch pad
        float puffSize, edgeReach, movingSpeed;

        void Setup(Material material)
        {
            this.material = material;

            mesh = new Mesh { name = "Smoke Trails" };
            mesh.MarkDynamic();
            propertyBlock = new MaterialPropertyBlock();

            // The compositor draws the mesh itself; without it the trails go straight onto the screen from here
            if (Compositor.Available)
                Compositor.Add(this);
            else
            {
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                meshRenderer = SetupRenderer(gameObject, material);
            }

            WorldView.main.positionOffset.OnChange += Position;
        }

        static MeshRenderer SetupRenderer(GameObject gameObject, Material material)
        {
            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.sortingLayerName = "Default";
            meshRenderer.sortingOrder = 5; // over parts and flames, under terrain, reentry heat and engine glows
            meshRenderer.enabled = false;
            return meshRenderer;
        }

        void OnDestroy()
        {
            if (WorldView.main != null)
                WorldView.main.positionOffset.OnChange -= Position;

            Compositor.Remove(this);
            ClearTrails();
            Destroy(mesh);
        }

        bool HasSmoke => vertexCount > 0;

        // Widens min and max to take in where the smoke's mesh lies on the camera's viewport
        void ExtendViewport(Camera camera, ref Vector2 min, ref Vector2 max)
        {
            Bounds bounds = mesh.bounds;
            Matrix4x4 toWorld = transform.localToWorldMatrix;
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = new(i % 2 == 0 ? bounds.min.x : bounds.max.x, i < 2 ? bounds.min.y : bounds.max.y, bounds.center.z);
                Vector2 viewport = camera.WorldToViewportPoint(toWorld.MultiplyPoint3x4(corner));
                min = Vector2.Min(min, viewport);
                max = Vector2.Max(max, viewport);
            }
        }

        static class Compositor
        {
            static readonly List<SmokeTrails> renderers = new();
            static CommandBuffer buffer;
            static Camera attached;
            static MeshRenderer quad, airborneQuad;
            static Material material, airborneMaterial;
            static bool? split;
            static readonly RenderTargetIdentifier[] targets = new RenderTargetIdentifier[2];
            static Texture2D none;
            static bool? available;

            public static bool Available
            {
                get
                {
                    available ??= SystemInfo.SupportsBlendingOnRenderTextureFormat(RenderTextureFormat.ARGBHalf) && UnityEngine.Resources.Load<Shader>("Effects/SmokeComposite") != null;
                    return available.Value;
                }
            }

            public static void Add(SmokeTrails renderer)
            {
                if (buffer == null)
                {
                    buffer = new CommandBuffer { name = "Smoke Trails" };
                    Camera.onPreRender += Prepare;
                }

                renderers.Add(renderer);
            }

            public static void Remove(SmokeTrails renderer) => renderers.Remove(renderer);

            // Whether the smoke still in the air can be kept apart from what's down on the ground, in a second texture alongside
            static bool Split => split ??= SystemInfo.supportedRenderTargetCount >= 2;

            static MeshRenderer Quad
            {
                get
                {
                    if (quad == null)
                        quad = CreateQuad("Smoke Composite", ref material, false);
                    return quad;
                }
            }

            // The smoke not yet down on the ground, drawn with the flames, so it goes behind the launch pad and the parts
            static MeshRenderer AirborneQuad
            {
                get
                {
                    if (airborneQuad != null)
                        return airborneQuad;

                    airborneQuad = CreateQuad("Smoke Composite (Airborne)", ref airborneMaterial, true);
                    airborneQuad.sortingOrder = 0;
                    airborneQuad.transform.position = new Vector3(0, 0, -1); // nearer than the flames, so drawn after them in their queue
                    airborneMaterial.renderQueue = AirborneQueue;
                    return airborneQuad;
                }
            }

            static MeshRenderer CreateQuad(string name, ref Material quadMaterial, bool airborne)
            {
                // Its corners are clip-space, and its bounds reach far enough that it's never culled
                Mesh mesh = new() { name = name };
                mesh.SetVertices(new[] { new Vector3(-1, -1), new Vector3(1, -1), new Vector3(1, 1), new Vector3(-1, 1) });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0, false);
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e9f);

                GameObject gameObject = new(name);
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                if (quadMaterial == null)
                {
                    quadMaterial = new Material(UnityEngine.Resources.Load<Shader>("Effects/SmokeComposite"));
                    quadMaterial.SetFloat(AirborneLayer, airborne ? 1 : 0);
                }
                return SetupRenderer(gameObject, quadMaterial);
            }

            static Texture2D None
            {
                get
                {
                    if (none != null)
                        return none;
                    none = new Texture2D(1, 1, TextureFormat.RGBAHalf, false);
                    none.SetPixel(0, 0, Color.clear);
                    none.Apply();
                    return none;
                }
            }

            // Refills the buffer for the camera about to draw the world, with the trails and the origin where they are now
            static void Prepare(Camera camera)
            {
                Camera world = WorldView.main != null && WorldView.main.worldCamera != null ? WorldView.main.worldCamera.camera : null;
                if (camera != world || world == null)
                {
                    Shader.SetGlobalTexture(Accumulated, None);
                    Shader.SetGlobalTexture(Airborne, None);
                    Shader.SetGlobalVector(ScreenRect, new Vector4(0, 0, 1, 1));
                    return;
                }

                if (attached != camera)
                {
                    if (attached != null)
                        attached.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, buffer);
                    camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, buffer);
                    attached = camera;
                }

                int resolution = Mathf.Max(downsample, 1);
                bool any = false, grounded = false;
                Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
                foreach (SmokeTrails renderer in renderers)
                    if (renderer != null && renderer.HasSmoke)
                    {
                        any = true;
                        grounded |= renderer.hasGround;
                        renderer.ExtendViewport(camera, ref min, ref max);
                    }

                // The composite only covers the smoke, and a texel of the accumulation more
                Vector2 margin = new Vector2(resolution + 1f, resolution + 1f) / new Vector2(camera.pixelWidth, camera.pixelHeight);
                min = Vector2.Max(min - margin, Vector2.zero);
                max = Vector2.Min(max + margin, Vector2.one);
                any &= max.x > min.x && max.y > min.y;

                // Without smoke in front of the pad, all of it goes in the airborne texture alone
                bool split = Split && grounded;
                buffer.Clear();
                Quad.enabled = any && (split || !Split);
                AirborneQuad.enabled = any && Split;
                if (!any)
                    return;

                FilterMode filter = resolution > 1 ? FilterMode.Bilinear : FilterMode.Point;
                int target = Split ? Airborne : Accumulated;
                buffer.GetTemporaryRT(target, -resolution, -resolution, 0, filter, RenderTextureFormat.ARGBHalf);
                if (split)
                {
                    buffer.GetTemporaryRT(Accumulated, -resolution, -resolution, 0, filter, RenderTextureFormat.ARGBHalf);
                    targets[0] = Airborne;
                    targets[1] = Accumulated;
                    buffer.SetRenderTarget(targets, targets[0]);
                }
                else
                    buffer.SetRenderTarget(target);
                buffer.ClearRenderTarget(false, true, Color.clear);
                buffer.SetGlobalFloat(Resolution, resolution);
                buffer.SetGlobalVector(ScreenRect, new Vector4(min.x, min.y, max.x, max.y));
                foreach (SmokeTrails renderer in renderers)
                    if (renderer != null && renderer.HasSmoke)
                        buffer.DrawMesh(renderer.mesh, renderer.transform.localToWorldMatrix, renderer.material, 0, split ? AccumulateSplitPass : AccumulatePass, renderer.propertyBlock);
                buffer.SetGlobalTexture(target, target);
                if (split)
                    buffer.SetGlobalTexture(Accumulated, Accumulated);
                buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            }
        }

        void ClearTrails()
        {
            foreach (SmokeTrail trail in trails)
                trail.Removed = true;
            trails.Clear();
        }

        // Keeps the mesh where it belongs when the origin moves after it was built
        void Position()
        {
            transform.position = WorldView.ToLocalPosition(anchor);
        }

        // The smoke moves on physics steps, as the craft and the WorldView's origin do, or the fast smoke by the craft would judder against it
        void FixedUpdate()
        {
            if (WorldView.main == null || WorldTime.main == null || !Enabled)
                return;

            Planet planet = WorldView.main.ViewLocation.planet;
            float deltaTime = WorldTime.FixedDeltaTime;
            foreach (SmokeTrail trail in trails)
                if (trail.planet == planet)
                    trail.Move(deltaTime);
        }

        void LateUpdate()
        {
            if (WorldView.main == null || WorldTime.main == null)
                return;

            if (!Enabled)
            {
                ClearTrails();
                vertexCount = 0;
                Upload();
                if (meshRenderer != null)
                    meshRenderer.enabled = false;
                return;
            }

            Planet planet = WorldView.main.ViewLocation.planet;
            double now = WorldTime.main.worldTime;

            for (int i = trails.Count - 1; i >= 0; i--)
            {
                SmokeTrail trail = trails[i];
                if (trail.planet == planet)
                    trail.Simulate(now);

                if (trail.planet != planet || trail.IsGone)
                {
                    trail.Removed = true;
                    trails.RemoveAt(i);
                }
            }

            int total = 0;
            foreach (SmokeTrail trail in trails)
                total += trail.points.Count;
            for (int i = 0; total > MaxTotalPoints && i < trails.Count; i++)
                total -= trails[i].RemoveOldest(total - MaxTotalPoints);

            BuildMesh(now);
        }


        void BuildMesh(double now)
        {
            vertexCount = 0;
            hasGround = false;
            boundsMin = Vector2.positiveInfinity;
            boundsMax = Vector2.negativeInfinity;

            anchor = WorldView.main.positionOffset.Value;
            transform.position = Vector3.zero;
            puffSize = Mathf.Max(material.GetFloat(PuffSize), 1e-3f);
            movingSpeed = Mathf.Max(material.GetFloat(MovingSpeed), 1e-3f);
            edgeReach = Mathf.Lerp(1, EdgeReach, Mathf.Clamp01(material.GetFloat(EdgeBreakup))) * ReachMargin;

            Camera camera = WorldView.main.worldCamera != null ? WorldView.main.worldCamera.camera : null;
            if (camera != null && camera.isActiveAndEnabled && !WorldView.main.scaledSpace.Value)
            {
                Rect view = GetView(camera);
                foreach (SmokeTrail trail in trails)
                    AddTrail(trail, now, view);
            }

            Upload();
            if (meshRenderer != null)
                meshRenderer.enabled = vertexCount > 0;
            if (vertexCount == 0)
                return;

            // The noise is laid in the air, so it's anchored to where this origin is in the world
            propertyBlock.SetVector(NoiseOrigin, new Vector4(Fraction(anchor.x / NoiseTile), Fraction(anchor.y / NoiseTile)));
            propertyBlock.SetFloat(NoiseTileProperty, (float)NoiseTile);

            // Lit from above wherever the view is around the planet
            Vector2 up = anchor.sqrMagnitude > 0 ? (Vector2)(anchor / anchor.magnitude) : Vector2.up;
            propertyBlock.SetVector(Sun, Quaternion.Euler(0, 0, SunTurn) * up);
            if (meshRenderer != null)
                meshRenderer.SetPropertyBlock(propertyBlock);
        }

        // Hands the vertices built this frame to the mesh. Its quads always join their corners the same way, so the indices are set once for as many as it can draw
        void Upload()
        {
            if (!indicesSet)
            {
                ushort[] quads = new ushort[MaxSegments * 6];
                for (int i = 0, v = 0; i < quads.Length; i += 6, v += 4)
                {
                    quads[i] = (ushort)v;
                    quads[i + 1] = (ushort)(v + 1);
                    quads[i + 2] = (ushort)(v + 2);
                    quads[i + 3] = (ushort)v;
                    quads[i + 4] = (ushort)(v + 2);
                    quads[i + 5] = (ushort)(v + 3);
                }
                mesh.SetIndexBufferParams(quads.Length, IndexFormat.UInt16);
                mesh.SetIndexBufferData(quads, 0, 0, quads.Length, UpdateFlags);
                indicesSet = true;
            }

            if (vertexCount > vertexCapacity)
            {
                vertexCapacity = Mathf.Min(Mathf.Max(vertexCount, vertexCapacity * 2, 1024), MaxSegments * 4);
                mesh.SetVertexBufferParams(vertexCapacity, Layout);
            }
            if (vertexCount > 0)
                mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, UpdateFlags);

            Bounds bounds = vertexCount > 0 ? new Bounds((boundsMin + boundsMax) / 2, boundsMax - boundsMin) : default;
            if (mesh.subMeshCount != 1)
                mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, vertexCount / 4 * 6) { bounds = bounds, vertexCount = vertexCount }, UpdateFlags);
            mesh.bounds = bounds;
        }

        static float Fraction(double value) => (float)(value - System.Math.Floor(value));

        // How far the smoke at both ends has carried the noise, in tiles, less the whole tiles they share - which the tiling can't show - so it stays small
        static Vector4 GetCarried(Double2 newer, Double2 older, double tile)
        {
            double wholeX = System.Math.Floor(newer.x / tile), wholeY = System.Math.Floor(newer.y / tile);
            return new Vector4((float)(newer.x / tile - wholeX), (float)(newer.y / tile - wholeY), (float)(older.x / tile - wholeX), (float)(older.y / tile - wholeY));
        }

        // How far the puffs the smoke carried are squeezed or stretched along a segment, where the ground turned it aside or it piles into a stop:
        // 0 within 1.4 times either way, 1 from twice (aground) or past AirSqueeze, and only where that covers a quarter of a puff
        float GetDistortion(Node newer, Node older, Vector2 delta, float length)
        {
            bool grounded = newer.grounded || older.grounded;
            float weight = grounded ? 1 : 1 - Mathf.Clamp01(Mathf.Min(newer.speed, older.speed) / movingSpeed);
            if (weight <= 0)
                return 0;

            float texture = (delta - (Vector2)(older.carried - newer.carried)).magnitude;
            float stretch = Mathf.Abs(Mathf.Log(Mathf.Max(texture, 1e-4f) / Mathf.Max(length, 1e-4f), 2));
            float distorted = grounded ? Mathf.Clamp01(stretch * 2 - 1) : Mathf.Clamp01(stretch - AirSqueeze);
            float puff = puffSize * Mathf.Min(newer.radius, older.radius);
            return weight * distorted * Mathf.Clamp01(Mathf.Max(texture, length) * 4 / Mathf.Max(puff, 1e-3f));
        }

        // The part of the z = 0 plane the camera sees, with a margin
        static Rect GetView(Camera camera)
        {
            float depth = camera.orthographic ? 1 : Mathf.Abs(camera.transform.position.z);

            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = camera.ViewportToWorldPoint(new Vector3(i % 2, i / 2, depth));
                min = Vector2.Min(min, corner);
                max = Vector2.Max(max, corner);
            }

            Vector2 margin = (max - min) * 0.1f;
            return Rect.MinMaxRect(min.x - margin.x, min.y - margin.y, max.x + margin.x, max.y + margin.y);
        }

        // Walks the trail from its head back to its oldest point
        void AddTrail(SmokeTrail trail, double now, Rect view)
        {
            List<SmokeTrail.Point> points = trail.points;
            int last = points.Count - 1;
            if (last < 0)
                return;

            Node newer = new(trail.Emitting ? trail.head : points[last], anchor, now, trail.keepVisible);
            float headRadius = newer.radius;
            float fromHead = 0;
            float slope = Mathf.Max(ConeSlope, trail.widening);
            Vector2 cone = new(1 / slope, headRadius);
            Vector2 direction = Vector2.down;

            for (int i = trail.Emitting ? last : last - 1; i >= 0; i--)
            {
                Node older = new(points[i], anchor, now, trail.keepVisible);
                older = older.Within(headRadius + (fromHead + Vector2.Distance(newer.position, older.position)) * slope);
                Vector2 open = new(!trail.Emitting && i == last - 1 ? 1 : 0, i == 0 ? 1 : 0); // ends no other segment takes over from
                fromHead += AddSegment(newer, older, fromHead, cone, open, trail, view, ref direction);
                newer = older;
            }

            // The smoke the oldest point has taken in, as a blob around it as wide as it has spread, and far enough from the head that its cone
            // doesn't cut into it
            if (points[0].mass > 0 && !points[0].grounded)
            {
                Node pile = new(points[0], anchor, now, trail.keepVisible);
                AddSegment(pile, pile, fromHead + 2 * pile.radius + trail.fadeIn, new Vector2(0, 1e9f), Vector2.one, trail, view, ref direction);
            }
        }

        // One quad per segment, reaching past both ends by the cap its smoke is spread over
        float AddSegment(Node newer, Node older, float fromHead, Vector2 cone, Vector2 open, SmokeTrail trail, Rect view, ref Vector2 direction)
        {
            Vector2 delta = older.position - newer.position;
            float length = delta.magnitude;
            if (length > 1e-4f)
                direction = delta / length;

            if (vertexCount >= MaxSegments * 4)
                return length;

            float cap = Mathf.Max(newer.radius, older.radius);

            // Half-widths at its newer and older end: a trapezoid just around the furthest the edges reach, past each end at that end's
            // radius and in between running from one to the other
            float newerReach = edgeReach * newer.radius;
            float olderReach = edgeReach * (length > 0 ? older.radius : newer.radius);
            float spread = Mathf.Abs(newerReach - olderReach) * cap / Mathf.Max(length + cap, 1e-6f);
            float newerWidth = newerReach > olderReach ? newerReach + spread : newerReach;
            float olderWidth = olderReach > newerReach ? olderReach + spread : olderReach;
            float halfWidth = Mathf.Max(cap, Mathf.Max(newerWidth, olderWidth));

            // Off screen
            Vector2 min = Vector2.Min(newer.position, older.position) - Vector2.one * halfWidth;
            Vector2 max = Vector2.Max(newer.position, older.position) + Vector2.one * halfWidth;
            if (min.x > view.xMax || max.x < view.xMin || min.y > view.yMax || max.y < view.yMin)
                return length;

            // Too faint to see, even where the noise doubles it up
            float peak = newer.mass * Mathf.Max(newer.fade, older.fade) / Mathf.Max(length, cap * 4 / 3) * 0.9375f / Mathf.Min(newer.radius, older.radius);
            if (peak * 2 < MinDepth)
                return length;

            Vector2 across = new(-direction.y, direction.x);
            Vector4 shape = new(length, cap, newer.radius, older.radius);
            Vector4 smoke = new(newer.mass, newer.fade, older.fade, trail.fadeIn);
            Vector4 carried = GetCarried(newer.carried, older.carried, NoiseTile);
            Vector3 speed = new(newer.speed, older.speed, GetDistortion(newer, older, delta, length));
            Vector2 ground = new(newer.ground, older.ground);
            hasGround |= newer.ground > 0 || older.ground > 0;

            if (vertexCount + 4 > vertices.Length)
                System.Array.Resize(ref vertices, Mathf.Min(vertices.Length * 2, MaxSegments * 4));

            // A segment too short to span its cap is drawn as a round blob, whose edges reach as far along it as across
            float ends = length > cap * 1e-3f ? cap : Mathf.Max(cap, Mathf.Max(newerWidth, olderWidth));

            for (int corner = 0; corner < 4; corner++)
            {
                float t = corner < 2 ? -ends : length + ends;
                float width = corner < 2 ? newerWidth : olderWidth;
                float y = corner == 0 || corner == 3 ? -width : width;
                Vector2 position = newer.position + direction * t + across * y;
                boundsMin = Vector2.Min(boundsMin, position);
                boundsMax = Vector2.Max(boundsMax, position);

                vertices[vertexCount++] = new Vertex
                {
                    position = position,
                    color = trail.color,
                    segment = new Vector4(t, y, fromHead + t, older.random),
                    shape = shape,
                    smoke = smoke,
                    carried = carried,
                    ends = open,
                    speed = speed,
                    cone = cone,
                    ground = ground,
                };
            }

            return length;
        }

        readonly struct Node
        {
            public readonly Vector2 position;
            public readonly float radius;
            public readonly float fade;
            public readonly float mass;
            public readonly Double2 carried;
            public readonly float speed;
            public readonly float random;
            public readonly bool grounded;
            public readonly float ground; // how far it has come out in front of the launch pad, once the ground has turned it aside

            public Node(in SmokeTrail.Point point, Double2 anchor, double now, float keepVisible)
            {
                position = point.position - anchor;
                carried = point.carried;
                speed = point.velocity.magnitude;
                random = point.random;
                grounded = point.grounded;
                ground = point.grounded ? Mathf.Clamp01(point.groundedFor / GroundShowTime) : 0;
                radius = point.GetRadius(now);
                fade = point.GetFade(now);
                mass = point.mass * Mathf.Pow(radius / Mathf.Max(point.GetSwollenRadius(now), 1e-4f), keepVisible);
            }

            Node(in Node node, float radius)
            {
                this = node;
                this.radius = radius;
            }

            // The same smoke squeezed into no wider than maxRadius
            public Node Within(float maxRadius) => radius > maxRadius ? new Node(this, maxRadius) : this;
        }
    }


    // A chain of points let go into the air, oldest first, with a live head at the flame while it's still emitting
    public class SmokeTrail
    {
        public struct Point
        {
            public Double2 position; // planet-centred
            public Double2 carried;  // how far it has carried the noise with it, so its texture moves with it
            public Vector2 velocity; // relative to the planet, and so to the air
            public double time;      // world time it was let go
            public float radius;     // when let go
            public float swell;      // how much wider it puffs out just after
            public float swellTime;  // s it takes to puff out about two thirds of that
            public float expansion;  // m its radius has grown by a minute on
            public float drag;       // 1/s its velocity through the air dies away at
            public float lifetime;
            public float mass;       // smoke between it and the next older point, as optical depth · m²
            public float side;       // which way it runs off along the ground
            public float spread; // of the speed it hit the ground with
            public float random;
            public bool held;        // still in the flame, which carries it on down itself until it lets go where the flame ends
            public float along;      // m down the flame from the nozzle, while held
            public float hidden;     // how much of it doesn't show yet when let go: in thin air it only shows as it puffs out
            public bool grounded;    // the ground has turned it aside
            public float groundedFor; // s since, while that matters to SmokeTrails
            public float grown;      // m wider than its age makes it, where smoke merged into it would otherwise pile up past PileDepth

            public float GetRadius(double now)
            {
                // Turbulence spreads smoke ever faster as it grows: its width goes as age^1.5
                float minutes = Age(now) / 60;
                return GetSwollenRadius(now) + expansion * minutes * Mathf.Sqrt(minutes);
            }

            public float GetSwollenRadius(double now) => radius * GetSwelling(now) + grown;

            // How much wider it has puffed out than it was let go
            public float GetSwelling(double now) => 1 + swell * (1 - Mathf.Exp(-Age(now) / swellTime));

            public float GetFade(double now)
            {
                float shown = 1 - hidden * Mathf.Exp(-Age(now) / swellTime);
                return shown * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.5f, 1, Age(now) / lifetime)));
            }
            float Age(double now) => Mathf.Max(0, (float)(now - time));

            // Two bunched points as one, where and as old as their smoke is, weighted by how much each carries, so a pile keeps its place
            // as smoke joins it; and spread wider where its smoke would pile up past PileDepth. Smoke still in the flame stays where it is
            public static Point Merge(in Point older, in Point newer, double now)
            {
                Point merged = newer;
                merged.mass = older.mass + newer.mass;
                if (older.held || newer.held)
                    return merged;

                float w = merged.mass > 0 ? older.mass / merged.mass : 0.5f;
                merged.position = newer.position + (older.position - newer.position) * w;
                merged.carried = newer.carried + (older.carried - newer.carried) * w;
                merged.velocity = Vector2.Lerp(newer.velocity, older.velocity, w);
                merged.grown = Mathf.Lerp(newer.grown, older.grown, w);
                merged.time = newer.time + (older.time - newer.time) * w;
                merged.radius = Mathf.Lerp(newer.radius, older.radius, w);
                merged.swell = Mathf.Lerp(newer.swell, older.swell, w);
                merged.swellTime = Mathf.Lerp(newer.swellTime, older.swellTime, w);
                merged.expansion = Mathf.Lerp(newer.expansion, older.expansion, w);
                merged.drag = Mathf.Lerp(newer.drag, older.drag, w);
                merged.lifetime = Mathf.Lerp(newer.lifetime, older.lifetime, w);
                merged.hidden = Mathf.Lerp(newer.hidden, older.hidden, w);
                merged.spread = Mathf.Lerp(newer.spread, older.spread, w);
                if (w > 0.5f)
                {
                    merged.side = older.side;
                    merged.grounded = older.grounded;
                    merged.groundedFor = older.groundedFor;
                }

                // A blob of it peaks at 3 / π mass / r² deep
                float needed = Mathf.Sqrt(0.955f * merged.mass / PileDepth), wide = merged.GetRadius(now);
                if (wide < needed)
                    merged.grown += needed - wide;
                return merged;
            }
        }

        const float EmitInterval = 0.1f; // s
        const int MaxPoints = 2000;
        const float MergeDistance = 0.7f; // of the radius
        const float PileDepth = 10; // optical depth merged smoke spreads out rather than pile up past
        const float MinSpeed = 0.5f; // m/s, below which a point settles
        const float MinSpread = -0.05f, MaxSpread = 1.1f; // run-off speed, of the speed it hit the ground with
        const float SpreadPeriod = 2; // s per sweep from fastest to slowest
        const float GroundLift = 1.06f; // how much of that speed bounces it back up

        public readonly Planet planet;
        public readonly float side; // which way along the ground its smoke runs
        public readonly float spreadPhase; // where in the spread sweep it is at time 0
        public readonly List<Point> points = new();
        public Point head;
        public Color32 color;
        public float fadeIn; // m the smoke takes to thicken in from its head
        public float keepVisible; // how much of its opacity the smoke keeps as it spreads, 0 thinning out with its width as mass would
        public float widening; // m its flame widens per m back from the smoke's head, which the smoke may keep up with

        public bool Emitting { get; private set; } = true;
        public bool Removed { get; internal set; }
        public bool IsGone => !Emitting && points.Count == 0;

        double lastFeed = double.NaN, lastEmit;
        float pendingMass;

        public SmokeTrail(Planet planet, float side, float spreadPhase)
        {
            this.planet = planet;
            this.side = side;
            this.spreadPhase = spreadPhase;
        }

        // Moves the head to where the smoke shows up in the flame, and every so often leaves a point there for the flame to carry on.
        // A trail fed alongside a lead from the same flame wears the lead's texture, so where their smoke overlaps it shows one pattern.
        public void Feed(Double2 position, Vector2 velocity, float radius, float along, float swell, float swellTime, float expansion, float drag, float lifetime, float hidden, float massRate, double now, SmokeTrail lead = null)
        {
            if (!IsFinite(position) || !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(radius) || !float.IsFinite(massRate))
                return;
            if (!double.IsNaN(lastFeed))
                pendingMass += massRate * Mathf.Max(0, (float)(now - lastFeed));
            lastFeed = now;
            List<Point> textured = (lead ?? this).points;

            head = new Point
            {
                position = position,
                velocity = velocity,
                time = now,
                radius = radius,
                swell = swell,
                swellTime = Mathf.Max(swellTime, 0.01f),
                expansion = expansion,
                drag = Mathf.Max(drag, 0.01f),
                lifetime = Mathf.Max(lifetime, 0.01f),
                mass = pendingMass,
                side = side,
                spread = GetSpread(now),
                random = Random.value,
                carried = textured.Count > 0 ? textured[^1].carried : Double2.zero, // picks up where its neighbour's texture is, unstretched
                held = true,
                along = along,
                hidden = Mathf.Clamp01(hidden),
            };

            if (points.Count == 0 || now - lastEmit >= EmitInterval)
                Emit(now);
        }

        float GetSpread(double now)
        {
            double sweep = now / SpreadPeriod + spreadPhase;
            return Mathf.Lerp(MaxSpread, MinSpread, (float)(sweep - System.Math.Floor(sweep)));
        }

        public void End()
        {
            if (Emitting && points.Count > 0 && pendingMass > 0)
                Emit(lastFeed);
            Emitting = false;

            for (int i = points.Count - 1; i >= 0 && points[i].held; i--)
            {
                Point point = points[i];
                point.held = false;
                points[i] = point;
            }
        }

        public int RemoveOldest(int count)
        {
            count = Mathf.Min(count, points.Count);
            points.RemoveRange(0, count);
            return count;
        }

        static bool IsFinite(Double2 value) => double.IsFinite(value.x) && double.IsFinite(value.y);

        void Emit(double now)
        {
            points.Add(head);
            head.mass = pendingMass = 0;
            lastEmit = now;
        }

        // Points that may have hit the ground this step, whose heights are then sampled all at once
        static readonly List<int> grounding = new();
        static readonly List<Point> groundingFrom = new();
        static readonly List<double> groundingAngles = new();

        // Carries the smoke that's been let go on through the air, one physics step
        public void Move(float deltaTime)
        {
            if (!(deltaTime > 0))
                return;

            grounding.Clear();
            groundingFrom.Clear();
            groundingAngles.Clear();

            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.grounded && point.groundedFor < 1)
                {
                    point.groundedFor += deltaTime;
                    points[i] = point;
                }

                float speed = point.velocity.magnitude;
                if (point.held || !(speed > MinSpeed))
                    continue;

                // Drag in proportion to speed, so smoke thrown out of a fast flame keeps its momentum for a while
                float decay = Mathf.Exp(-point.drag * deltaTime);
                Vector2 moved = point.velocity * ((1 - decay) / point.drag);
                point.position += moved;
                point.carried += moved;
                point.velocity *= decay;

                if (!IsFinite(point.position) || !IsFinite(point.carried))
                {
                    point.position = points[i].position;
                    point.carried = points[i].carried;
                    point.velocity = Vector2.zero;
                }
                else if (MayHitGround(point))
                {
                    grounding.Add(i);
                    groundingFrom.Add(points[i]);
                    groundingAngles.Add(point.position.AngleRadians);
                    points[i] = point;
                    continue;
                }

                StopIfSlow(ref point);
                points[i] = point;
            }

            if (grounding.Count == 0)
                return;

            double[] heights = planet.GetTerrainHeightAtAngles(groundingAngles.ToArray(), true);
            for (int n = 0; n < grounding.Count; n++)
            {
                int i = grounding[n];
                Point point = points[i];
                StayAboveGround(ref point, heights[n]);

                if (!IsFinite(point.position))
                {
                    point.position = groundingFrom[n].position;
                    point.carried = groundingFrom[n].carried;
                    point.velocity = Vector2.zero;
                }
                StopIfSlow(ref point);
                points[i] = point;
            }
        }

        static void StopIfSlow(ref Point point)
        {
            if (point.velocity.sqrMagnitude < MinSpeed * MinSpeed || !float.IsFinite(point.velocity.x) || !float.IsFinite(point.velocity.y))
                point.velocity = Vector2.zero;
        }

        public void Simulate(double now)
        {
            int expired = 0;
            while (expired < points.Count && now - points[expired].time > points[expired].lifetime)
                expired++;
            expired = Mathf.Max(expired, points.Count - MaxPoints);
            if (expired > 0)
                points.RemoveRange(0, expired);

            // The oldest takes in smoke that comes to rest with it in the air and shows it as its own, so a pile stays one point
            if (points.Count >= 2)
            {
                Point a = points[0], b = points[1];
                double reach = MergeDistance * b.GetRadius(now);
                if (!a.held && !b.held && !a.grounded && !b.grounded && (b.position - a.position).sqrMagnitude < reach * reach)
                {
                    points[0] = Point.Merge(a, b, now);
                    points.RemoveAt(1);
                }
            }

            // Points bunched up closer than their smoke is wide add nothing but overdraw. Walked from the newest, with the points
            // kept packed at the end and the next older one checked against the last kept
            int count = points.Count;
            if (count < 3)
                return;

            int kept = count - 1;
            for (int i = count - 2; i >= 1; i--)
            {
                Point a = points[i - 1], b = points[i], c = points[kept];
                double limit = MergeDistance * b.GetRadius(now);
                limit *= limit;

                if ((c.position - a.position).sqrMagnitude < limit && (b.position - a.position).sqrMagnitude < limit)
                {
                    if (b.grounded || c.grounded)
                    {
                        c.mass += b.mass;
                        points[kept] = c;
                    }
                    else
                        points[kept] = Point.Merge(b, c, now);
                }
                else
                    points[--kept] = b;
            }
            points[--kept] = points[0];
            if (kept > 0)
                points.RemoveRange(0, kept);
        }

        bool MayHitGround(in Point point)
        {
            return point.position.magnitude <= planet.Radius + System.Math.Max(planet.maxTerrainHeight, 0) + 1 && Double2.Dot(point.position, (Double2)point.velocity) < 0;
        }

        // Smoke blown into the ground spreads out along it instead
        void StayAboveGround(ref Point point, double terrainHeight)
        {
            double radius = point.position.magnitude;
            double ground = planet.Radius + terrainHeight;
            if (radius >= ground)
                return;

            Double2 up = point.position / radius;
            point.position = up * ground;
            point.grounded = true;

            Vector2 normal = up;
            float into = Vector2.Dot(point.velocity, normal);
            if (into < 0)
                point.velocity += normal * (-into * GroundLift) + new Vector2(normal.y, -normal.x) * (-into * point.side * point.spread);
        }
    }
}
