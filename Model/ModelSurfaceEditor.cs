#if UNITY_EDITOR
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace SFS.Parts.Modules
{
    [CustomEditor(typeof(ModelSurface))]
    public class ModelSurfaceEditor : OdinEditor
    {
        ModelSurface model;

        void OnSceneGUI()
        {
            model = target as ModelSurface;

            if (model != null && model.isActiveAndEnabled)
            {
                EditorGUI.BeginChangeCheck();
                Undo.RecordObject(model, "Edit Model Surface");
                ModelPolygonEditor.DrawAddButtons(model.meshes, model.points.Last().points, model.transform);
                if (EditorGUI.EndChangeCheck())
                    EditorUtility.SetDirty(model);

                foreach (ModelSurface.Surface surface in model.points)
                    ModelPolygonEditor.DrawLines(surface.points, surface.loop, model.transform, surface == model.points.Last()? Color.red : Color.green);
            }
        }
    }
}
#endif