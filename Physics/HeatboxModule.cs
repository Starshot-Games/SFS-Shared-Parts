using System;
using System.Collections.Generic;
using System.Linq;
using SFS.Variables;
using SFS.World;
using SFS.World.Drag;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SFS.Parts.Modules
{
    public class HeatboxModule : MonoBehaviour
    {
        public Vector2 position;
        
        // -1 - heatbox extends from position upwards
        // 0 - heatbox centered on position
        // 1 - heatbox extends from position upwards
        public float sizingAnchor;
        public Vector2 size;
        
        // in case some engine needs a non-linear response curve
        public AnimationCurve heatboxSizeCurve = AnimationCurve.Linear(0, 0, 1, 1);
        public Double_Reference sizePercent;
        public Bool_Reference enableHeat;

        private ThrustBase engine;

        void Start()
        {
            partsLayerMask = LayerMask.GetMask("Parts");
            engine = gameObject.GetComponentInParent<ThrustBase>();

            if (!cacheInitialized)
            {
                SceneManager.sceneUnloaded += _ =>
                {
                    cache.Clear(); // invalidate the cache when we leave the build/world scene
                };
                
                cacheInitialized = true;
            }
        }

        private static bool cacheInitialized;
        private static Dictionary<Collider2D, (HeatModuleBase hmb, bool isNull)> cache = new();
        private static int partsLayerMask;
        
        void FixedUpdate()
        {
            if (GameManager.main == null) return;

            if (!Calculate(out _, out var center, out var globSize, out var angle)) return;
            
            // ReSharper disable once Unity.PreferNonAllocApi
            // the non-allocating method is deprecated, and the alternative doesn't provide what we want
            foreach (Collider2D col in Physics2D.OverlapBoxAll(center, globSize, angle, partsLayerMask))
            {
                HeatModuleBase hmb;
                if (!cache.TryGetValue(col, out var tuple))
                {
                    hmb = col.GetComponentInParent<HeatModuleBase>();
                    cache[col] = tuple = (hmb, hmb == null);
                }
                else
                {
                    hmb = tuple.hmb;
                }
                if (tuple.isNull) continue;
                
                try
                {
                    if (hmb.IgnoredHeatboxes.Contains(this)) continue;

                    hmb.Heat();

                    if (hmb is Part part && part.Rocket != engine.Rocket)
                    {
                        part.ApplyPushing(engine);
                    }
                }
                catch (NullReferenceException)
                {
                    if (hmb != null)
                        throw; // rethrow if some unrelated error got caught here
                    
                    cache[col] = (hmb, true);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!Calculate(out var points, out _, out _, out _)) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(points[0], points[1]);
            Gizmos.DrawLine(points[1], points[2]);
            Gizmos.DrawLine(points[2], points[3]);
            Gizmos.DrawLine(points[3], points[0]);
        }

        bool Calculate(
            out Vector2[] globalPoints, 
            out Vector2 globalCenter,
            out Vector2 globalSize,
            out float angle)
        {
            globalPoints = Array.Empty<Vector2>();
            globalCenter = Vector2.zero;
            globalSize = Vector2.zero;
            angle = 0f;
            
            float effectiveThrottle = heatboxSizeCurve.Evaluate((float)sizePercent.Value);
            if (effectiveThrottle == 0f || !enableHeat.Value) return false; // return false when the heatbox shouldn't be there at all
            
            Vector2 localSize = new Vector2(size.x, size.y * effectiveThrottle);
            Vector2 localCenter = position + Vector2.up * localSize * (sizingAnchor * 0.5f);
            
            // ReSharper disable once InconsistentNaming
            const int POINTS_LENGTH = 4;
            
            Vector2[] points = new Vector2[POINTS_LENGTH]
            {
                localCenter + localSize * new Vector2(-0.5f, -0.5f),
                localCenter + localSize * new Vector2(0.5f, -0.5f),
                localCenter + localSize * new Vector2(0.5f, 0.5f),
                localCenter + localSize * new Vector2(-0.5f, 0.5f)
            };

            Matrix4x4 ltw = transform.localToWorldMatrix;
            
            globalCenter = Vector2.zero;
            for (int i = 0; i < POINTS_LENGTH; i++) // array length should always be 4
            {
                Vector2 transformed = ltw.MultiplyPoint3x4(points[i]);
                globalCenter += transformed;
                points[i] = transformed;
            }
            globalCenter *= 1f / POINTS_LENGTH;

            angle = Vector2.SignedAngle(Vector2.right, points[1] - points[0]);
            globalPoints = points;
            globalSize = new Vector2(
                (points[1] - points[0]).magnitude,
                (points[2] - points[1]).magnitude
            );

            return true;
        }
    }
}