using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SFS.Variables;
using SFS.World;
using SFS.World.Drag;
using UnityEngine;
using UnityEngine.Profiling;
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

        private readonly static AnimationCurve defaultCurve = AnimationCurve.Linear(0, 0, 1, 1);
        
        public Double_Reference sizePercent;
        public Bool_Reference enableHeat;

        private ThrustBase engine;
        private int staggerIndex;
        private int physFrameIndex;
        private List<Collider2D> resultsCache = new();

        private const int STAGGER = 4;
        
        void Start()
        {
            partsLayerMask = LayerMask.GetMask("Parts");

            contactFilter = new ContactFilter2D()
            {
                useLayerMask = true,
                layerMask = partsLayerMask
            };
            
            engine = gameObject.GetComponentInParent<ThrustBase>();

            if (!cacheInitialized)
            {
                SceneManager.sceneUnloaded += _ =>
                {
                    cache.Clear(); // invalidate the cache when we leave the build/world scene
                };
                
                cacheInitialized = true;
            }

            staggerIndex = Mathf.Abs(engineId++) % STAGGER;
        }

        private static bool cacheInitialized;
        private static Dictionary<Collider2D, (HeatModuleBase hmb, bool isNull)> cache = new();
        private static ContactFilter2D contactFilter;
        
        private static int partsLayerMask;
        private static bool gameManagerIsNull = true;
        private static int updateFrameIndex;
        private static int engineId;
        
        void FixedUpdate()
        {
            physFrameIndex++;
            
            if (Time.frameCount > updateFrameIndex)
            {
                gameManagerIsNull = GameManager.main == null;
                updateFrameIndex = Time.frameCount;
            }

            if (gameManagerIsNull) return;

            if (!Calculate(out _, out _, out _, out _, out var center, out var globSize, out var angle)) return;
            
            if (physFrameIndex % STAGGER == staggerIndex) Physics2D.OverlapBox(center, globSize, angle, contactFilter, resultsCache);
            foreach (Collider2D col in resultsCache)
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
            if (!Calculate(out var p0, out var p1, out var p2, out var p3, out _, out _, out _)) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p3);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);
        }

        bool Calculate(
            out Vector2 p0,
            out Vector2 p1,
            out Vector2 p2,
            out Vector2 p3,
            out Vector2 globalCenter,
            out Vector2 globalSize,
            out float angle)
        {
            globalCenter = Vector2.zero;
            globalSize = Vector2.zero;
            angle = 0f;
            
            p0 = Vector2.zero;
            p1 = Vector2.zero;
            p2 = Vector2.zero;
            p3 = Vector2.zero;
            
            if (!enableHeat.Value || sizePercent.Value == 0.0) return false; // return false when the heatbox shouldn't be there at all
            
            Vector2 localSize = new Vector2(size.x, size.y * (float)sizePercent.Value);
            Vector2 localCenter = position + Vector2.up * localSize * (sizingAnchor * 0.5f);
            
            p0 = localCenter + localSize * new Vector2(-0.5f, -0.5f);
            p1 = localCenter + localSize * new Vector2(0.5f, -0.5f);
            p2 = localCenter + localSize * new Vector2(0.5f, 0.5f);
            p3 = localCenter + localSize * new Vector2(-0.5f, 0.5f);

            Matrix4x4 ltw = transform.localToWorldMatrix;
            
            globalCenter = Vector2.zero;
            p0 = ltw.MultiplyPoint3x4(p0);
            globalCenter += p0;
            p1 = ltw.MultiplyPoint3x4(p1);
            globalCenter += p1;
            p2 = ltw.MultiplyPoint3x4(p2);
            globalCenter += p2;
            p3 = ltw.MultiplyPoint3x4(p3);
            globalCenter += p3;
            
            globalCenter *= 1f / 4;

            angle = Vector2.SignedAngle(Vector2.right, p1 - p0);
            globalSize = new Vector2(
                (p1 - p0).magnitude,
                (p2 - p1).magnitude
            );

            return true;
        }
    }
}