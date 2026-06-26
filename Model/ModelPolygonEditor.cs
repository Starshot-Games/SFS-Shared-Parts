#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace SFS.Parts.Modules
{
    [CustomEditor(typeof(ModelPolygon))]
    public class ModelPolygonEditor : OdinEditor
    {
        ModelPolygon modelPolygon;

        void OnSceneGUI()
        {
            modelPolygon = target as ModelPolygon;

            if (modelPolygon != null && modelPolygon.isActiveAndEnabled)
            {
                if (modelPolygon.view || modelPolygon.edit)
                    DrawLines(modelPolygon.points, true, modelPolygon.transform, Color.cyan);
                
                if (modelPolygon.edit)
                {
                    EditorGUI.BeginChangeCheck();
                    Undo.RecordObject(modelPolygon, "Edit Model Polygon");
                    DrawAddButtonsWithUsed(modelPolygon.meshes, modelPolygon.points, modelPolygon.transform);
                    if (EditorGUI.EndChangeCheck())
                        EditorUtility.SetDirty(modelPolygon);
                }
            }
        }

        public static void DrawAddButtonsWithUsed(MeshFilter[] meshes, List<Vector2> points, Transform transform)
        {
            foreach (MeshFilter meshFilter in meshes)
            foreach (Vector3 vertice in meshFilter.sharedMesh.vertices)
            {
                Vector3 vert = meshFilter.transform.TransformPoint(vertice);
                
                if (vert.z > -0.01f)
                {
                    Color c = points.Any(p => (p - (Vector2)vertice).magnitude < 0.001f)? Color.green : Color.blue;
                    
                    if (MyHandles.DrawButton(meshFilter.transform, vertice, 0.1f, c))
                        points.Add(transform.InverseTransformPoint(vert));
                }
            }
        }
        
        public static void DrawAddButtons(MeshFilter[] meshes, List<Vector2> points, Transform transform)
        {
            foreach (MeshFilter meshFilter in meshes)
            foreach (Vector3 vertice in meshFilter.sharedMesh.vertices)
            {
                Vector3 vert = meshFilter.transform.TransformPoint(vertice);
                
                if (vert.z > -0.01f)
                {
                    Color c = Color.green;
                    
                    if (MyHandles.DrawButton(meshFilter.transform, vertice, 0.1f, c))
                        points.Add(transform.InverseTransformPoint(vert));
                }
            }
        }
        
        public static void DrawLines(List<Vector2> points, bool loop, Transform transform, Color c)
        {
            for (int i = 0; i < points.Count - (loop ? 0 : 1); i++)
            {
                Handles.color = c;
                Handles.DrawLine(transform.TransformPoint(points[i]), transform.TransformPoint(points[(i + 1) % points.Count]), 3);
            }
        }
    }
}
#endif