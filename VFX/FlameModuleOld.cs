/*using System;
using System.Linq;
using UnityEngine;

namespace SFS.Parts.Modules
{
    [ExecuteInEditMode]
    public class FlameModuleOld : MonoBehaviour
    {
        // Ref
        public MeshFilter meshFilter;

        // State
        [Range(0, 1)] public float throttle, vacuum;
        
        // Setup
        public float startWidth;
        public AnimationCurve ground, vac;
        public Vector3 scaleGround = Vector3.one, scaleVac = Vector3.one;
        public float edgeFixed, edgePercent;
        public float textureAmount = 1;
        public float opacityPower = 3;
        public int count;

        //public Gradient color;  // color.Evaluate(t) * 
        
        
        void Start()
        {
            meshFilter = gameObject.GetComponent<MeshFilter>();
        }
        
        void Update()
        {
            Vector2[] g = GetPoints(ground, scaleGround);
            Vector2[] v = GetPoints(vac, scaleVac);
            Vector2[] points = new Vector2[count];
            
            for (int i = 0; i < count; i++)
                points[i] = Vector2.Lerp(g[i], v[i], vacuum);
            
            //CreateMesh(points);
        }

        Vector2[] GetPoints(AnimationCurve curve, Vector3 scale)
        {
            float width = curve.keys.Length > 0? curve.keys.Last().time : 1;
            
            Vector2[] positions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float t = (width - ((float)i / (count - 1) * width)) * throttle;
                positions[i] = new Vector2(startWidth, 0) + new Vector2(curve.Evaluate(t) * scale.x, -t * scale.y) * scale.z;
            }
            
            return positions;
        }

        void CreateMesh(Vector2[] points)
        {
            int quadCount_H = 4;
            int quadCount_V = points.Length - 1;
            int verticeCount = quadCount_H * quadCount_V * 4;
            float uvX = UnityEngine.Random.Range(0f, 1f);
            
            Vector3[] vertices = new Vector3[verticeCount];
            Color[] colors = new Color[verticeCount];
            Vector3[] uv = new Vector3[verticeCount];
            int[] indices = new int[verticeCount];

            Color[] colors_H = { new Color(1, 1, 1, 0), new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.7f), new Color(1, 1, 1, 0) };
            float[] uv_H = { 0f * textureAmount + uvX, 0.25f * textureAmount + uvX, 0.5f * textureAmount + uvX, 0.75f * textureAmount + uvX, 1f * textureAmount + uvX };
            Func<float, float>[] pos_H = { x => -x * (1 + edgePercent) - edgeFixed, x => -x, x => 0, x => x, x => x * (1 + edgePercent) + edgeFixed };

            Color[] color_V = new Color[points.Length];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                color_V[i] = new Color(1, 1, 1, Mathf.Pow(t, opacityPower));
            }

            int ii = 0;
            for (int h = 0; h < quadCount_H; h++)
            {
                for (int v = 0; v < quadCount_V; v++)
                {
                    float uv_Y_A = (float)(v + 1) / (count - 1);
                    float uv_Y_B = (float)v / (count - 1);

                    Vector2 p1 = points[v + 1];
                    Vector2 p2 = points[v];
                    
                    vertices[ii + 0] = new Vector2(pos_H[h].Invoke(p1.x), p1.y);
                    vertices[ii + 1] = new Vector2(pos_H[h + 1].Invoke(p1.x), p1.y);
                    vertices[ii + 2] = new Vector2(pos_H[h + 1].Invoke(p2.x), p2.y);
                    vertices[ii + 3] = new Vector2(pos_H[h].Invoke(p2.x), p2.y);
                
                    float[] uv_M = UV_Utility.GetQuadM(vertices[ii + 0], vertices[ii + 1], vertices[ii + 2], vertices[ii + 3]);

                    uv[ii + 0] = new Vector3(uv_H[h], uv_Y_A, 1) * uv_M[0];
                    uv[ii + 1] = new Vector3(uv_H[h + 1], uv_Y_A, 1) * uv_M[1];
                    uv[ii + 2] = new Vector3(uv_H[h + 1], uv_Y_B, 1) * uv_M[2];
                    uv[ii + 3] = new Vector3(uv_H[h], uv_Y_B, 1) * uv_M[3];
                    
                    colors[ii + 0] = colors_H[h] * color_V[v + 1];
                    colors[ii + 1] = colors_H[h + 1] * color_V[v + 1];
                    colors[ii + 2] = colors_H[h + 1] * color_V[v];
                    colors[ii + 3] = colors_H[h] * color_V[v];
                    
                    ii += 4;
                }
            }

            for (int i = 0; i < verticeCount; i++)
                indices[i] = i;
            
            
            if (meshFilter.sharedMesh == null)
                meshFilter.sharedMesh = new Mesh();
            
            // Apply mesh
            Mesh mesh = meshFilter.sharedMesh;
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Quads, 0);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.RecalculateBounds();
        }
    }
}
*/