using System;
using System.Linq;
using SFS;
using SFS.Parts;
using SFS.Parts.Modules;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Parts
{
    public class PartVisualizer : MonoBehaviour
    {
        public bool loadBuildSceneOnStart;
        void Start()
        {
            if (loadBuildSceneOnStart)
                Base.sceneLoader.LoadBuildScene(false);
        }
        
        [Space(50)]
        public ViewMode viewMode;
        
        Color IsNone => viewMode == ViewMode.None ? Color.lightGray : Color.gray;
        Color IsBuildColliders => viewMode == ViewMode.BuildColliders ? Color.green : Color.gray;
        Color IsAttachmentSurfaces => viewMode == ViewMode.AttachmentSurfaces ? Color.green : Color.gray;
        Color IsPhysicsColliders => viewMode == ViewMode.PhysicsColliders ? Color.green : Color.gray;
        Color IsCenterOfMass => viewMode == ViewMode.CenterOfMass ? Color.green : Color.gray;
        Color IsClickHitboxes => viewMode == ViewMode.ClickHitboxes ? Color.green : Color.gray;
        Color IsMagnets => viewMode == ViewMode.Magnets ? Color.green : Color.gray;
        Color IsAdaptTriggers => viewMode == ViewMode.AdaptTriggers ? Color.green : Color.gray;
        Color IsNozzleCoverHitbox => viewMode == ViewMode.NozzleCoverHitbox ? Color.green : Color.gray;
        Color IsHeatHitbox => viewMode == ViewMode.HeatHitbox ? Color.green : Color.gray;
        
        [PropertySpace]
        [Button(ButtonSizes.Large), GUIColor(nameof(IsNone))] void None() => viewMode = ViewMode.None;

        [Title("Build")]
        [Button(ButtonSizes.Large), GUIColor(nameof(IsBuildColliders))] void BuildColliders() => viewMode = ViewMode.BuildColliders;
        [Button(ButtonSizes.Large), GUIColor(nameof(IsAttachmentSurfaces))] void AttachmentSurfaces() => viewMode = ViewMode.AttachmentSurfaces;
        [Button(ButtonSizes.Large), GUIColor(nameof(IsMagnets))] void Magnets() => viewMode = ViewMode.Magnets;
        [Button(ButtonSizes.Large), GUIColor(nameof(IsAdaptTriggers))] void AdaptTriggers() => viewMode = ViewMode.AdaptTriggers;

        [Title("General")]
        [Button(ButtonSizes.Large), GUIColor(nameof(IsClickHitboxes))] void ClickHitboxes() => viewMode = ViewMode.ClickHitboxes;

        [Title("Physics")]
        [Button(ButtonSizes.Large), GUIColor(nameof(IsPhysicsColliders))] void PhysicsColliders() => viewMode = ViewMode.PhysicsColliders;
        [Button(ButtonSizes.Large), GUIColor(nameof(IsCenterOfMass))] void CenterOfMass() => viewMode = ViewMode.CenterOfMass;

        [Title("Engines")]
        [Button(ButtonSizes.Large), GUIColor(nameof(IsNozzleCoverHitbox))] void NozzleCoverHitbox() => viewMode = ViewMode.NozzleCoverHitbox;
        [Button(ButtonSizes.Large), GUIColor(nameof(IsHeatHitbox))] void HeatHitbox() => viewMode = ViewMode.HeatHitbox;
        

        #if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Fix
            GetComponentsInChildren<SurfaceData>().ForEach(x => x.Output());
            Handles.DrawLine(Vector3.zero, Vector3.zero);
            
            switch (viewMode)
            {
                // Build
                case ViewMode.BuildColliders:
                    Handles.color = Color.cyan;
                    GetComponentsInChildren<PolygonData>().Where(x => x.BuildCollider_IncludeInactive).ForEach(a => DrawPolygon(a, false, true));
                    break;
                
                case ViewMode.AttachmentSurfaces:
                    Handles.color = Color.yellow;
                    GetComponentsInChildren<SurfaceData>().Where(x => x.attachmentSurfaces).ForEach(x => DrawSurface(x, false));
                    break;

                case ViewMode.Magnets:
                    Gizmos.color = Color.blue;
                    GetComponentsInChildren<MagnetModule>().ForEach(magnet =>
                        magnet.GetSnapPointsWorld().ForEach(point => Gizmos.DrawSphere(point, 0.1f)));
                    break;

                case ViewMode.AdaptTriggers:
                    Gizmos.color = Color.yellowGreen;
                    GetComponentsInChildren<AdaptTriggerModule>().ForEach(module =>
                    {
                        foreach (AdaptTriggerPoint point in module.points)
                        {
                            Vector3 worldPos = module.transform.TransformPoint(point.position.Value);
                            Gizmos.DrawSphere(worldPos, 0.1f);
                            Handles.color = Color.magenta;
                            Handles.DrawLine(worldPos, worldPos + module.transform.TransformDirection(point.normal) * 0.3f, 2);
                        }
                    });
                    break;
                
                // Click hitbox
                case ViewMode.ClickHitboxes:
                    Handles.color = Color.magenta;
                    GetComponentsInChildren<PolygonData>().Where(x => x.Click).ForEach(a => DrawPolygon(a, false, true));
                    break;

                // Game physics
                case ViewMode.PhysicsColliders:
                    Handles.color = Color.green;
                    GetComponentsInChildren<SurfaceCollider>().ForEach(x => DrawSurface(x.surfaces, true));
                    GetComponentsInChildren<PolygonCollider>().Where(x => x.polygon != null).ForEach(x => DrawPolygon(x.polygon, true, true));
                    GetComponentsInChildren<PolygonData>().Where(x => x.PhysicsCollider_IncludeInactive).ForEach(x => DrawPolygon(x, true, true));
                    break;
                
                case ViewMode.CenterOfMass:
                    Gizmos.color = Color.green;
                    GetComponentsInChildren<Part>().Select(part => part.transform.TransformPoint(part.centerOfMass.Value))
                        .ForEach(com => Gizmos.DrawSphere(com, 0.2f));
                    break;
                
                // Engines section
                case ViewMode.NozzleCoverHitbox:
                    Handles.color = Color.red;
                    GetComponentsInChildren<IsCoveredModule>().ForEach(e => DrawSurface(e.surface, false));
                    GetComponentsInChildren<BoosterModule>().Where(b => b.surfaceForCover != null).ForEach(e => DrawSurface(e.surfaceForCover, true));
                    break;
                
                case ViewMode.HeatHitbox:
                    Handles.color = Color.orangeRed;
                    GetComponentsInChildren<BoosterModule>().ForEach(b => DrawBoxCollider(b.heatHolder.GetComponent<BoxCollider2D>()));
                    GetComponentsInChildren<EngineModule>().ForEach(e =>
                    {
                        if (e.multipleNozzles)
                        {
                            foreach (HeatHitbox hitbox in e.heatHitboxes)
                            {
                                DrawBoxCollider(hitbox.heatHolder.GetComponent<BoxCollider2D>());   
                                DrawBoxCollider(hitbox.heatHitbox.GetComponent<BoxCollider2D>());   
                            }
                        }
                        else
                        {
                            DrawBoxCollider(e.heatHolder.GetComponent<BoxCollider2D>());
                            DrawBoxCollider(e.heatHolder.GetComponentInChildren<BoxCollider2D>());
                        }
                    });
                    break;
            }
        }
        
        void DrawPolygon(PolygonData polygon, bool reducedResolution, bool drawPipe)
        {
            if (drawPipe && polygon is PipeData x)
                DrawPipe(x, reducedResolution);
            else
                DrawSurface(polygon, reducedResolution);
        }
        void DrawSurface(SurfaceData polygon, bool reducedResolution)
        {
            // Draws black outline
            Color c = Handles.color;
            Handles.color = new Color(0, 0, 0, 0.7f);
            (reducedResolution? polygon.surfacesFast : polygon.surfaces).ForEach(y => y.GetSurfacesWorld().ForEach(z => Handles.DrawLine(z.start, z.end, 4)));
            Handles.color = c;
            (reducedResolution? polygon.surfacesFast : polygon.surfaces).ForEach(y => y.GetSurfacesWorld().ForEach(z => Handles.DrawLine(z.start, z.end, 1.5f)));
        }
        void DrawPipe(PipeData pipe, bool reducedResolution)
        {
            Color c = Handles.color;
            Handles.color = new Color(1, 1, 1, 1f);

            UnityEngine.Transform t = pipe.transform;
            float depthM = -pipe.depthMultiplier * (Math.Abs(t.localEulerAngles.y - 180) < 0.1f ? -1 : 1);
                
            pipe.pipe.points.ForEach(a => Handles.DrawDottedLine(t.TransformPoint(a.GetPosition(-1)), t.TransformPoint(a.GetPosition(1)), 4));

            for (int i = 0; i < pipe.pipe.points.Count - 1; i++)
            {
                PipePoint a = pipe.pipe.points[i];
                PipePoint b = pipe.pipe.points[i + 1];
                Handles.DrawDottedLine(t.TransformPoint(a.position.ToVector3(a.width.magnitude / 2 * depthM)), t.TransformPoint(b.position.ToVector3(b.width.magnitude / 2 * depthM)), 5);
            }

            Handles.color = c;
            DrawSurface(pipe, reducedResolution);
        }

        void DrawBoxCollider(BoxCollider2D collider)
        {
            if (collider == null)
                return;

            UnityEngine.Transform t = collider.transform;

            Vector2 size = collider.size;
            Vector2 offset = collider.offset;

            Vector3 bl = t.TransformPoint(offset + new Vector2(-size.x, -size.y) * 0.5f);
            Vector3 tl = t.TransformPoint(offset + new Vector2(-size.x,  size.y) * 0.5f);
            Vector3 tr = t.TransformPoint(offset + new Vector2( size.x,  size.y) * 0.5f);
            Vector3 br = t.TransformPoint(offset + new Vector2( size.x, -size.y) * 0.5f);
            
            Handles.DrawLine(bl, tl, 3);
            Handles.DrawLine(tl, tr, 3);
            Handles.DrawLine(tr, br, 3);
            Handles.DrawLine(br, bl, 3);
        }
        
        
        const string Key = "SceneView.handleLineThickness";
        

        [PropertySpace(50)]
        [Button(ButtonSizes.Medium)]
        void ApplyPrefabChanges()
        {
            for (int i = 0; i < transform.childCount; i++)
                PrefabUtility.ApplyPrefabInstance(transform.GetChild(i).gameObject, InteractionMode.UserAction);
        }
        
        [PropertySpace]
        [Button]
        void RoundPosition()
        {
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).position = Math_Utility.Round(transform.GetChild(i).position, 0.5f);
        }
        [Button]
        void DisableEdit()
        {
            GetComponentsInChildren<CustomPolygon>().ForEach(a => a.edit = a.view = false);
            GetComponentsInChildren<CustomSurfaces>().ForEach(a => a.edit = a.view = false);
            GetComponentsInChildren<CustomPipe>().ForEach(a => a.edit = a.view = false);
        }

        [PropertySpace]
        [Button, HorizontalGroup]
        void ToInteriorView() => GetComponentsInChildren<InteriorModule>(true).ForEach(a => a.gameObject.SetActive(a.layerType == InteriorModule.LayerType.Interior));
        [PropertySpace]
        [Button, HorizontalGroup]
        void ToExteriorView() => GetComponentsInChildren<InteriorModule>(true).ForEach(a => a.gameObject.SetActive(a.layerType == InteriorModule.LayerType.Exterior));
        [Button]
        void ResetViewMode() => GetComponentsInChildren<InteriorModule>(true).ForEach(a => a.gameObject.SetActive(true));
        
        #endif

        [PropertySpace]
        [Button] void ShowAllFlames() => ToggleFlames(true);
        [Button] void HideAllFlames() => ToggleFlames(false);
        void ToggleFlames(bool toggle)
        {
            debugThrottle = toggle ? 1 : 0;
            ApplyDebugFlames();
        }

        [PropertySpace]
        [Range(0, 1), OnValueChanged(nameof(ApplyDebugFlames))] public float debugThrottle = 1;
        [Range(0, 1), OnValueChanged(nameof(ApplyDebugFlames))] public float debugVacuum;
        void ApplyDebugFlames()
        {
            GetComponentsInChildren<EngineEffects>(true).ForEach(e => e.SetDebugState(debugThrottle, debugVacuum));
        }
        
        
        [Serializable]
        public enum ViewMode
        {
            None,
            
            BuildColliders,
            AttachmentSurfaces,
            Magnets,
            AdaptTriggers,
            
            ClickHitboxes,
            
            PhysicsColliders,
            CenterOfMass,
            
            NozzleCoverHitbox,
            HeatHitbox,
        }
    }
}