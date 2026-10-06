using System.Collections.Generic;
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
        static readonly int NoiseScale = Shader.PropertyToID("_NoiseScale");
        static readonly int DetailScale = Shader.PropertyToID("_DetailScale");

        const float MinDepth = 0.002f; // segments fainter than this are left out
        const float QuadWidth = 1.5f;  // of the radius, leaving room for the shader's ragged edges

        // Hard limits, whatever is flying: past them the oldest smoke goes first
        const int MaxTrails = 256;
        const int MaxTotalPoints = 20000;
        const int MaxSegments = 16000; // drawn per frame, which also keeps the mesh within 16-bit indices

        static readonly Dictionary<Material, SmokeTrails> renderers = new();

        public static SmokeTrail StartTrail(Material material, Planet planet)
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

            SmokeTrail trail = new(planet);
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

        readonly List<Vector3> vertices = new();
        readonly List<Color32> colors = new();
        readonly List<Vector4> segments = new();
        readonly List<Vector4> shapes = new();
        readonly List<Vector4> smokes = new();
        readonly List<Vector4> carriedA = new();
        readonly List<Vector4> carriedB = new();
        readonly List<Vector4> speeds = new();
        double noiseScale, detailScale;
        readonly List<int> indices = new();

        void Setup(Material material)
        {
            this.material = material;

            mesh = new Mesh { name = "Smoke Trails" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.sortingLayerName = "Default";
            meshRenderer.sortingOrder = 5; // over parts and flames, under terrain, reentry heat and engine glows
            meshRenderer.enabled = false;

            propertyBlock = new MaterialPropertyBlock();
            WorldView.main.positionOffset.OnChange += Position;
        }

        void OnDestroy()
        {
            if (WorldView.main != null)
                WorldView.main.positionOffset.OnChange -= Position;

            ClearTrails();
            Destroy(mesh);
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
                mesh.Clear();
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
            vertices.Clear();
            colors.Clear();
            segments.Clear();
            shapes.Clear();
            smokes.Clear();
            carriedA.Clear();
            carriedB.Clear();
            speeds.Clear();
            indices.Clear();

            anchor = WorldView.main.positionOffset.Value;
            transform.position = Vector3.zero;
            noiseScale = Mathf.Max(material.GetFloat(NoiseScale), 1e-3f);
            detailScale = Mathf.Max(material.GetFloat(DetailScale), 1e-3f);

            Camera camera = WorldView.main.worldCamera != null ? WorldView.main.worldCamera.camera : null;
            if (camera != null && camera.isActiveAndEnabled && !WorldView.main.scaledSpace.Value)
            {
                Rect view = GetView(camera);
                foreach (SmokeTrail trail in trails)
                    AddTrail(trail, now, view);
            }

            mesh.Clear();
            meshRenderer.enabled = vertices.Count > 0;
            if (vertices.Count == 0)
                return;

            mesh.indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, segments);
            mesh.SetUVs(1, shapes);
            mesh.SetUVs(2, smokes);
            mesh.SetUVs(3, carriedA);
            mesh.SetUVs(4, carriedB);
            mesh.SetUVs(5, speeds);
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();

            // The noise is laid in the air, so it's anchored to where this origin is in the world
            propertyBlock.SetVector(NoiseOrigin, new Vector4(
                Fraction(anchor.x / noiseScale), Fraction(anchor.y / noiseScale),
                Fraction(anchor.x / detailScale), Fraction(anchor.y / detailScale)));
            meshRenderer.SetPropertyBlock(propertyBlock);
        }

        static float Fraction(double value) => (float)(value - System.Math.Floor(value));

        // How far the smoke at both ends has carried the noise, in tiles, less the whole tiles they share - which the tiling can't show - so it stays small
        static Vector4 GetCarried(Double2 newer, Double2 older, double tile)
        {
            double wholeX = System.Math.Floor(newer.x / tile), wholeY = System.Math.Floor(newer.y / tile);
            return new Vector4((float)(newer.x / tile - wholeX), (float)(newer.y / tile - wholeY), (float)(older.x / tile - wholeX), (float)(older.y / tile - wholeY));
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
            if (last < (trail.Emitting ? 0 : 1))
                return;

            Node newer = new(trail.Emitting ? trail.head : points[last], anchor, now, trail.keepVisible);
            float fromHead = 0;
            Vector2 direction = Vector2.down;

            for (int i = trail.Emitting ? last : last - 1; i >= 0; i--)
            {
                Node older = new(points[i], anchor, now, trail.keepVisible);
                fromHead += AddSegment(newer, older, fromHead, trail, view, ref direction);
                newer = older;
            }
        }

        // One quad per segment, reaching past both ends by the cap its smoke is spread over
        float AddSegment(Node newer, Node older, float fromHead, SmokeTrail trail, Rect view, ref Vector2 direction)
        {
            Vector2 delta = older.position - newer.position;
            float length = delta.magnitude;
            if (length > 1e-4f)
                direction = delta / length;

            if (vertices.Count >= MaxSegments * 4)
                return length;

            float cap = Mathf.Max(newer.radius, older.radius);
            float halfWidth = cap * QuadWidth;

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
            int start = vertices.Count;

            for (int corner = 0; corner < 4; corner++)
            {
                float t = corner < 2 ? -cap : length + cap;
                float y = corner == 0 || corner == 3 ? -halfWidth : halfWidth;

                vertices.Add(newer.position + direction * t + across * y);
                colors.Add(trail.color);
                segments.Add(new Vector4(t, y, fromHead + t, older.random));
                shapes.Add(new Vector4(length, cap, newer.radius, older.radius));
                smokes.Add(new Vector4(newer.mass, newer.fade, older.fade, trail.fadeIn));
                carriedA.Add(GetCarried(newer.carried, older.carried, noiseScale));
                carriedB.Add(GetCarried(newer.carried, older.carried, detailScale));
                speeds.Add(new Vector2(newer.speed, older.speed));
            }

            indices.Add(start);
            indices.Add(start + 1);
            indices.Add(start + 2);
            indices.Add(start);
            indices.Add(start + 2);
            indices.Add(start + 3);

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

            public Node(in SmokeTrail.Point point, Double2 anchor, double now, float keepVisible)
            {
                position = point.position - anchor;
                carried = point.carried;
                speed = point.velocity.magnitude;
                random = point.random;
                radius = point.GetRadius(now);
                fade = point.GetFade(now);
                mass = point.mass * Mathf.Pow(radius / Mathf.Max(point.GetSwollenRadius(now), 1e-4f), keepVisible);
            }
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
            public float expansion;  // m its radius has grown by a minute on
            public float drag;       // 1/s its velocity through the air dies away at
            public float lifetime;
            public float mass;       // smoke between it and the next older point, as optical depth · m²
            public float side;       // which way it runs off along the ground
            public float random;
            public bool held;        // still in the flame, which carries it on down itself until it lets go where the flame ends
            public float along;      // m down the flame from the nozzle, while held
            public float hidden;     // how much of it doesn't show yet when let go: in thin air it only shows as it puffs out

            public float GetRadius(double now)
            {
                // Turbulence spreads smoke ever faster as it grows: its width goes as age^1.5
                float minutes = Age(now) / 60;
                return GetSwollenRadius(now) + expansion * minutes * Mathf.Sqrt(minutes);
            }

            public float GetSwollenRadius(double now) => radius * (1 + swell * (1 - Mathf.Exp(-Age(now) / SwellTime)));

            public float GetFade(double now)
            {
                float shown = 1 - hidden * Mathf.Exp(-Age(now) / SwellTime);
                return shown * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.5f, 1, Age(now) / lifetime)));
            }
            float Age(double now) => Mathf.Max(0, (float)(now - time));
        }

        const float EmitInterval = 0.1f; // s
        const float SwellTime = 1.2f; // s
        const int MaxPoints = 2000;
        const float MergeDistance = 0.7f; // of the radius
        const float MinSpeed = 0.5f; // m/s, below which a point settles

        public readonly Planet planet;
        public readonly List<Point> points = new();
        public Point head;
        public Color32 color;
        public float fadeIn; // m the smoke takes to thicken in from its head
        public float keepVisible; // how much of its opacity the smoke keeps as it spreads, 0 thinning out with its width as mass would

        public bool Emitting { get; private set; } = true;
        public bool Removed { get; internal set; }
        public bool IsGone => !Emitting && points.Count == 0;

        double lastFeed = double.NaN, lastEmit;
        float pendingMass;
        float side = 1;

        public SmokeTrail(Planet planet) => this.planet = planet;

        // Moves the head to where the smoke shows up in the flame, and every so often leaves a point there for the flame to carry on
        public void Feed(Double2 position, Vector2 velocity, float radius, float along, float swell, float expansion, float drag, float lifetime, float hidden, float massRate, double now)
        {
            if (!IsFinite(position) || !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y) || !float.IsFinite(radius) || !float.IsFinite(massRate))
                return;
            if (!double.IsNaN(lastFeed))
                pendingMass += massRate * Mathf.Max(0, (float)(now - lastFeed));
            lastFeed = now;

            head = new Point
            {
                position = position,
                velocity = velocity,
                time = now,
                radius = radius,
                swell = swell,
                expansion = expansion,
                drag = Mathf.Max(drag, 0.01f),
                lifetime = Mathf.Max(lifetime, 0.01f),
                mass = pendingMass,
                side = side,
                random = Random.value,
                carried = points.Count > 0 ? points[^1].carried : Double2.zero, // picks up where its neighbour's texture is, unstretched
                held = true,
                along = along,
                hidden = Mathf.Clamp01(hidden),
            };

            if (points.Count == 0 || now - lastEmit >= EmitInterval)
                Emit(now);
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
            side = -side;
        }

        // Carries the smoke that's been let go on through the air, one physics step
        public void Move(float deltaTime)
        {
            if (!(deltaTime > 0))
                return;

            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                float speed = point.velocity.magnitude;
                if (point.held || !(speed > MinSpeed))
                    continue;

                // Drag in proportion to speed, so smoke thrown out of a fast flame keeps its momentum for a while
                float decay = Mathf.Exp(-point.drag * deltaTime);
                Vector2 moved = point.velocity * ((1 - decay) / point.drag);
                point.position += moved;
                point.carried += moved;
                point.velocity *= decay;
                StayAboveGround(ref point);

                if (!IsFinite(point.position) || !IsFinite(point.carried))
                {
                    point.position = points[i].position;
                    point.carried = points[i].carried;
                    point.velocity = Vector2.zero;
                }
                if (point.velocity.sqrMagnitude < MinSpeed * MinSpeed || !float.IsFinite(point.velocity.x) || !float.IsFinite(point.velocity.y))
                    point.velocity = Vector2.zero;
                points[i] = point;
            }
        }

        public void Simulate(double now)
        {
            int expired = 0;
            while (expired < points.Count && now - points[expired].time > points[expired].lifetime)
                expired++;
            expired = Mathf.Max(expired, points.Count - MaxPoints);
            if (expired > 0)
                points.RemoveRange(0, expired);

            // Points bunched up closer than their smoke is wide add nothing but overdraw
            for (int i = points.Count - 2; i >= 1; i--)
            {
                Point a = points[i - 1], b = points[i], c = points[i + 1];
                double limit = MergeDistance * b.GetRadius(now);
                limit *= limit;

                if ((c.position - a.position).sqrMagnitude < limit && (b.position - a.position).sqrMagnitude < limit)
                {
                    c.mass += b.mass;
                    points[i + 1] = c;
                    points.RemoveAt(i);
                }
            }
        }

        // Smoke blown into the ground spreads out along it instead
        void StayAboveGround(ref Point point)
        {
            double radius = point.position.magnitude;
            if (radius > planet.Radius + System.Math.Max(planet.maxTerrainHeight, 0) + 1 || Double2.Dot(point.position, (Double2)point.velocity) >= 0)
                return;

            double ground = planet.Radius + planet.GetTerrainHeightAtAngle(point.position.AngleRadians, true);
            if (radius >= ground)
                return;

            Double2 up = point.position / radius;
            point.position = up * ground;

            Vector2 normal = up;
            float into = Vector2.Dot(point.velocity, normal);
            if (into < 0)
            {
                float lift = 1 + 0.12f * (point.random * 7.31f % 1);
                float sideways = point.side * Mathf.Lerp(0.3f, 1, point.random);
                point.velocity += normal * (-into * lift) + new Vector2(normal.y, -normal.x) * (-into * sideways);
            }
        }
    }
}
