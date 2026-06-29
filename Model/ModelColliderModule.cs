using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    [HideMonoScript]
    public class ModelColliderModule : PolygonData, I_InitializePartModule
    {
        [Required] public MeshFilter mesh;

        [Tooltip("Prevents the flame heat hitbox from applying heating effects to the engine's own collider."), LabelText("Ignored Heat Hitbox")] public Collider2D ownEngineNozzle;
        protected override Collider2D OwnEngineNozzle => ownEngineNozzle;

        [BoxGroup("Detail Reduction", false), LabelText("Simplify Tolerance")] public float simplifyTolerance = 0.02f;
        [BoxGroup("Detail Reduction", false), LabelText("Merge Distance")] public float mergeDistance = 0.05f; // Bridge gaps between separate islands up to this size

        // Cached result of the expensive mesh -> outline computation (serialized)
        [HideInInspector, SerializeField] OutlineLoop[] outline = new OutlineLoop[0];

        int I_InitializePartModule.Priority => 15;
        void I_InitializePartModule.Initialize() => Output();
        void Reset()
        {
            attachmentSurfaces = false;
            mesh = GetComponent<MeshFilter>();
            ComputeOutline();
        }

        // Pushes the cached outline to the collider / click system // Cheap, runs at startup
        public override void Output()
        {
            SetData(new Polygon(this, LargestLoop()));
        }
        // The outline is normally a single connected island // Pick the largest if it isn't
        Vector2[] LargestLoop()
        {
            Vector2[] best = new Vector2[0];
            float biggest = -1;
            foreach (OutlineLoop loop in outline)
            {
                if (loop.points == null || loop.points.Length < 3)
                    continue;

                float area = Mathf.Abs(LoopArea(loop.points));
                if (area > biggest)
                {
                    biggest = area;
                    best = loop.points;
                }
            }
            return best;
        }

        // Expensive mesh -> outline computation // Only runs on Reset and on the button, never in real time
        [Button(ButtonSizes.Large)] public void ComputeOutline()
        {
            outline = BuildOutline().Select(points => new OutlineLoop { points = points }).ToArray();

            #if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
            #endif

            Output();
        }

        [Serializable]
        public struct OutlineLoop
        {
            public Vector2[] points;
        }


        // ---------- Polygon shape: union the mesh triangles, keep the biggest island, drop holes ----------
        // Uses Clipper2 for a robust, exact polygon union, then keeps the largest outer contour.
        const double Scale = 1000.0; // Fixed-point precision for Clipper (0.001 local units)

        List<Vector2[]> BuildOutline()
        {
            List<Vector2[]> result = new List<Vector2[]>();

            if (mesh == null || mesh.sharedMesh == null)
                return result;

            Vector3[] meshVertices = mesh.sharedMesh.vertices;
            int[] triangles = mesh.sharedMesh.triangles;
            Transform meshTransform = mesh.transform;

            // Each triangle becomes a CCW subject path in fixed-point space
            Paths64 subject = new Paths64(triangles.Length / 3);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 a = transform.InverseTransformPoint(meshTransform.TransformPoint(meshVertices[triangles[i]]));
                Vector2 b = transform.InverseTransformPoint(meshTransform.TransformPoint(meshVertices[triangles[i + 1]]));
                Vector2 c = transform.InverseTransformPoint(meshTransform.TransformPoint(meshVertices[triangles[i + 2]]));

                float cross = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
                if (Mathf.Abs(cross) < 1e-9f)
                    continue; // Degenerate

                if (cross < 0)
                    (b, c) = (c, b); // Force CCW

                subject.Add(new Path64 { Point(a), Point(b), Point(c) });
            }

            if (subject.Count == 0)
                return result;

            // Robust union of all triangles // NonZero merges the consistently-wound triangles
            Paths64 solution = Clipper.Union(subject, FillRule.NonZero);

            // Close gaps between nearby islands (inflate then deflate by the same amount)
            if (mergeDistance > 0f)
            {
                double delta = mergeDistance * 0.5 * Scale;
                solution = Clipper.InflatePaths(solution, delta, JoinType.Miter, EndType.Polygon);
                solution = Clipper.InflatePaths(solution, -delta, JoinType.Miter, EndType.Polygon);
            }

            // Keep the biggest outer contour // Holes are separate, smaller paths
            Path64 biggest = null;
            double biggestArea = 0;
            foreach (Path64 path in solution)
            {
                double area = Math.Abs(Clipper.Area(path));
                if (area > biggestArea)
                {
                    biggestArea = area;
                    biggest = path;
                }
            }

            if (biggest != null)
            {
                Vector2[] points = biggest.Select(p => new Vector2((float)(p.X / Scale), (float)(p.Y / Scale))).ToArray();
                points = Simplify(points, simplifyTolerance);
                if (points.Length >= 3)
                    result.Add(points);
            }

            return result;
        }
        static Point64 Point(Vector2 p) => new Point64((long)Math.Round(p.x * Scale), (long)Math.Round(p.y * Scale));
        static float LoopArea(IList<Vector2> loop)
        {
            float area = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i];
                Vector2 b = loop[(i + 1) % loop.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }


        // ---------- Detail reduction: Ramer-Douglas-Peucker over the closed loop ----------
        static Vector2[] Simplify(Vector2[] loop, float tolerance)
        {
            if (tolerance <= 0 || loop.Length < 4)
                return loop;

            // Split the loop at point 0 and the point farthest from it, then simplify each arc
            int farthest = 0;
            float maxDistance = -1;
            for (int i = 1; i < loop.Length; i++)
            {
                float distance = (loop[i] - loop[0]).sqrMagnitude;
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthest = i;
                }
            }

            List<int> keep = new List<int> { 0, farthest };
            Simplify(loop, 0, farthest, tolerance, keep);
            Simplify(loop, farthest, loop.Length, tolerance, keep); // Second arc wraps past the end

            return keep.Select(i => i % loop.Length).Distinct().OrderBy(i => i).Select(i => loop[i]).ToArray();
        }
        static void Simplify(Vector2[] loop, int first, int last, float tolerance, List<int> keep)
        {
            int length = loop.Length;
            Vector2 start = loop[first % length];
            Vector2 end = loop[last % length];

            float maxDistance = 0;
            int farthest = -1;
            for (int i = first + 1; i < last; i++)
            {
                float distance = DistanceToSegmentSqr(start, end, loop[i % length]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    farthest = i;
                }
            }

            if (farthest != -1 && maxDistance > tolerance * tolerance)
            {
                keep.Add(farthest % length);
                Simplify(loop, first, farthest, tolerance, keep);
                Simplify(loop, farthest, last, tolerance, keep);
            }
        }
        static float DistanceToSegmentSqr(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 line = b - a;
            float lengthSqr = line.x * line.x + line.y * line.y;
            if (lengthSqr == 0)
                return (point - a).sqrMagnitude;

            float t = ((point.x - a.x) * line.x + (point.y - a.y) * line.y) / lengthSqr;
            t = Mathf.Clamp01(t);
            return (point - (a + line * t)).sqrMagnitude;
        }


        // ---------- Raycast: read the depth encoded in the mesh (front-most vertex Z) ----------
        // Mirrors "Part 2d Model" shader, which uses the world-space vertex Z as depth.
        public override bool Raycast(UnityEngine.Object debugObject, Vector2 point, out float depth)
        {
            depth = BaseDepth;

            if (mesh == null || mesh.sharedMesh == null)
                return false;

            Vector3[] meshVertices = mesh.sharedMesh.vertices;
            int[] triangles = mesh.sharedMesh.triangles;
            Transform meshTransform = mesh.transform;

            // Mesh into collider-local space (keeps Z, which is the depth)
            Vector3[] local = new Vector3[meshVertices.Length];
            for (int i = 0; i < meshVertices.Length; i++)
                local[i] = transform.InverseTransformPoint(meshTransform.TransformPoint(meshVertices[i]));

            bool hit = false;
            float frontZ = float.NegativeInfinity;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = local[triangles[i]];
                Vector3 b = local[triangles[i + 1]];
                Vector3 c = local[triangles[i + 2]];

                if (!Barycentric(point, a, b, c, out float u, out float v, out float w))
                    continue;

                float z = a.z * u + b.z * v + c.z * w;
                if (z > frontZ)
                {
                    frontZ = z;
                    hit = true;
                }
            }

            if (!hit)
                return false;

            depth = BaseDepth + frontZ;
            return true;
        }
        // 2D barycentric weights of point in triangle a/b/c // Returns false if outside
        static bool Barycentric(Vector2 point, Vector2 a, Vector2 b, Vector2 c, out float u, out float v, out float w)
        {
            Vector2 v0 = b - a;
            Vector2 v1 = c - a;
            Vector2 v2 = point - a;

            float denominator = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(denominator) < 1e-12f)
            {
                u = v = w = 0;
                return false; // Degenerate
            }

            v = (v2.x * v1.y - v1.x * v2.y) / denominator;
            w = (v0.x * v2.y - v2.x * v0.y) / denominator;
            u = 1f - v - w;

            return u >= 0 && v >= 0 && w >= 0;
        }


        // ---------- Debug: draw the generated outline when selected (same style as PartVisualizer) ----------
        #if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Only when THIS object is itself selected — not when a parent is
            if (!UnityEditor.Selection.Contains(gameObject))
                return;
            
            foreach (OutlineLoop loopData in outline)
            {
                Vector2[] island = loopData.points;
                if (island == null || island.Length < 2)
                    continue;

                Vector3[] world = island.Select(p => (Vector3)transform.TransformPoint(p)).ToArray();

                // Black outline underlay, then colored line on top
                DrawLoop(world, new Color(0, 0, 0, 0.7f), 4);
                DrawLoop(world, Color.magenta, 1.5f);
            }
        }
        static void DrawLoop(Vector3[] world, Color color, float thickness)
        {
            UnityEditor.Handles.color = color;
            for (int i = 0; i < world.Length; i++)
                UnityEditor.Handles.DrawLine(world[i], world[(i + 1) % world.Length], thickness);
        }
        #endif
    }
}
