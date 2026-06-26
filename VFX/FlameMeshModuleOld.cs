/*using System;
using System.Linq;
using SFS.Builds;
using SFS.World;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;


// organise engine types
// launch smoke
// launch countdown, skipped if hit ignition or stage again
// fix flames feeling dusty
// fix render depth

// exponent for time falloff // DONE
// singe value for falloff // DONE
// scaled vertices // DONE



namespace SFS.Parts.Modules
{
    public class FlameMeshModuleOld : MonoBehaviour
    {
        static readonly int VacuumTex = Shader.PropertyToID("_VacuumTex");
        static readonly int AdditiveBlend = Shader.PropertyToID("_AdditiveBlend");
        
        // Ref
        [Required] public GameObject holder;
        [HideInInspector] public MeshFilter meshFilter;
        [HideInInspector] public MeshRenderer meshRenderer;

        // Setup
        [Space]
        public float nozzleWidth;
        //
        [Space]
        [FoldoutGroup("Ground"), InlineProperty, HideLabel, HideIf("Vac")] public Data groundData;
        [Space]
        [FoldoutGroup("Ground"), HideIf("Vac")] public int machDiamondCount = 10;
        [FoldoutGroup("Ground"), HideIf("Vac")] public float machDiamondOffset = 0.25f;
        //
        [Space]
        [FoldoutGroup("Vac"), InlineProperty, HideLabel] public Data vacuumData;
        [Space]
        [FoldoutGroup("Vac")] public float vacThrottleAdjust = 0.75f;
        //
        [Space]
        public int count;

        
        void OnValidate()
        {
            if (!Application.isPlaying)
                Update();
        }

        void Start()
        {
            meshFilter = holder.GetOrAddComponent<MeshFilter>();
            meshRenderer = holder.GetOrAddComponent<MeshRenderer>();
            meshFilter.sharedMesh = null; // Makes each flame create its own mesh
        }

        public bool Vac;
        
        float throttle, vacuum;
        public void Update()
        {
            if (Application.isPlaying)
            {
                if (GameManager.main != null || BuildManager.main != null)
                {
                    if (GameManager.main != null)
                        vacuum = Vac? 1 : Mathf.InverseLerp(0.05f, 0.8f, (float)(WorldView.main.ViewLocation.Height / WorldView.main.ViewLocation.planet.AtmosphereHeightPhysics));
                    
                    EngineModule engine = transform.GetComponentInParentTree<EngineModule>();
                    if (engine != null)
                        throttle = Mathf.MoveTowards(throttle, engine.throttle_Out.Value, Time.deltaTime / 0.15f);
                    else
                    {
                        BoosterModule booster = transform.GetComponentInParentTree<BoosterModule>();
                        if (booster != null)
                            throttle = Mathf.MoveTowards(throttle, booster.throttle_Out.Value, Time.deltaTime / 0.15f);
                    }
                }
            }
            
            // Mesh
            CreateMesh(GetPoints(),
                Vector2.Lerp(groundData.noiseScale, vacuumData.noiseScale, vacuum),
                Vector2.Lerp(groundData.textureScale, vacuumData.textureScale, vacuum),
                Mathf.Lerp(groundData.timeFalloff, vacuumData.timeFalloff, vacuum),
                Mathf.Lerp(groundData.expansionFalloff, vacuumData.expansionFalloff, vacuum));
            
            // Material
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetFloat(VacuumTex, Mathf.Lerp(0, 1, vacuum));
            propertyBlock.SetFloat(AdditiveBlend, Mathf.Lerp(groundData.additiveBlend, vacuumData.additiveBlend, vacuum));
            meshRenderer.SetPropertyBlock(propertyBlock);
        }
        
        Vector2[] GetPoints()
        {
            switch (vacuum)
            {
                case 0: return GetPoints(groundData, machDiamondCount, machDiamondOffset, false);
                case 1: return GetPoints(vacuumData, 1, 0, false);
            }
            
            Vector2[] _ground = GetPoints(groundData, machDiamondCount, machDiamondOffset, true);
            Vector2[] _vac = GetPoints(vacuumData, 1, 0, false);
            
            Vector2[] points = new Vector2[count];
            for (int i = 0; i < count; i++)
                points[i] = Vector2.Lerp(_ground[i], _vac[i], vacuum);

            return points;
        }
        Vector2[] GetPoints(Data data, int repeat, float offset, bool reduceMachDiamonds)
        {
            float size = data.curve.keys.Last().time - data.curve.keys.First().time;
            float height = size * repeat;
            offset *= size;
            float adjust = data.curve.Evaluate(0 + offset);

            Vector2[] positions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float t = (1 - (float)i / (count - 1)) * throttle;
                t = (t + t * t * 2) / 3;
                
                float width_Curve = (data.curve.Evaluate(t * height + offset) - adjust) * data.widthCurve.x;
                if (reduceMachDiamonds)
                    width_Curve *= Mathf.Lerp(1, 0, vacuum * 4); // Hides mach diamonds at vac above 25%
                
                positions[i] = new Vector2(nozzleWidth + (width_Curve + data.linearWidth * t) * data.scale, (-t * height * data.widthCurve.y) * data.scale);
            }

            return positions;
        }
        
        static int drawIndex;
        void CreateMesh(Vector2[] points, Vector2 noiseScale, Vector2 textureScale, float timeFalloff, float expansionFalloff)
        {
            drawIndex = (drawIndex + 1) % 1000;

            float height = Mathf.Max(-points.First().y, 0.001f);
            
            float uvX = Random.value; // Flame tex offset
            float uvY = Random.Range(0.1f, 0.5f);
            
            // Vertical data
            Color[] color_V = new Color[points.Length];
            Vector2[] points_Left = new Vector2[points.Length];
            Vector2[] points_Right = new Vector2[points.Length];
            
            float startArea = Mathf.Pow(nozzleWidth, expansionFalloff);
            //
            float endOpacity_Area = startArea / Mathf.Pow(points.First().x, expansionFalloff);
            float endOpacity_Falloff = 1 / Mathf.Exp(timeFalloff);
            //
            float endOpacity = endOpacity_Falloff * endOpacity_Area;
            
            // Points and color
            for (int i = count - 1; i >= 0; i--)
            {
                Vector2 point = points[i];
                float t = -point.y / height;
                
                // Color
                Color c = Color.Lerp(groundData.gradient.Evaluate(t), vacuumData.gradient.Evaluate(t), vacuum);
                c.a *= Mathf.Lerp(Mathf.Lerp(groundData.opacityMin, groundData.opacityMax, throttle), Mathf.Lerp(vacuumData.opacityMin, vacuumData.opacityMax, throttle), vacuum);

                float opacity_Area = startArea / Mathf.Pow(point.x, expansionFalloff);
                float opacity_Falloff = 1 / Mathf.Exp(timeFalloff * t);
                float opacity = opacity_Falloff * opacity_Area;
                c.a *= opacity - endOpacity * t;

                /*if (i > 0)
                {
                    // Draw opacity curves as debug lines, t = x, opacity = y
                    Vector2 p1 = points[i - 1];
                    Vector2 p2 = points[i];
                    
                    float t1 = -p1.y / height;
                    float t2 = -p2.y / height;
                    
                    float opacity_Area1 = startArea / Mathf.Pow(p1.x, expansionFalloff);
                    float opacity_Area2 = startArea / Mathf.Pow(p2.x, expansionFalloff);
                    
                    float opacity_Falloff1 = 1 / Mathf.Exp(timeFalloff * t1);
                    float opacity_Falloff2 = 1 / Mathf.Exp(timeFalloff * t2);
                    
                    float opacity1 = opacity_Falloff1 * opacity_Area1;
                    float opacity2 = opacity_Falloff2 * opacity_Area2;

                    opacity1 -= endOpacity * t1 * t1;
                    opacity2 -= endOpacity * t2 * t2;
                    
                    Debug.DrawLine(new Vector3(t1, opacity1, 0), new Vector3(t2, opacity2, 0), Color.red, 0.01f);
                    
                    // Draw area and falloff curves
                    Debug.DrawLine(new Vector3(t1, opacity_Area1, 0), new Vector3(t2, opacity_Area2, 0), Color.green, 0.01f);
                    Debug.DrawLine(new Vector3(t1, opacity_Falloff1, 0), new Vector3(t2, opacity_Falloff2, 0), Color.blue, 0.01f);
                }*/
                
/*
                color_V[i] = c;

                // Noise
                float noiseLeft = (Mathf.PerlinNoise(drawIndex, t * height / noiseScale.y) * 2 - 1) * noiseScale.x * t * throttle; // Reduces noise scale for throttle
                float noiseRight = (Mathf.PerlinNoise(-drawIndex, t * height / noiseScale.y) * 2 - 1) * noiseScale.x * t * throttle;
                
                // Points
                points_Left[i] = new Vector2(-point.x + noiseLeft, point.y);
                points_Right[i] = new Vector2(point.x + noiseRight, point.y);
            }
            
            // Quads
            int quadCount_V = points.Length - 1;
            int verticeCount = 1 * quadCount_V * 4;
            Vector3[] vertices = new Vector3[verticeCount];
            Color[] colors = new Color[verticeCount];
            Vector3[] uv0 = new Vector3[verticeCount];
            Vector3[] uv1 = new Vector3[verticeCount];
            //
            int ii = 0;
            for (int v = 0; v < quadCount_V; v++)
            {
                // Vertices
                vertices[ii + 0] = points_Left[v + 1];
                vertices[ii + 1] = points_Right[v + 1];
                vertices[ii + 2] = points_Right[v];
                vertices[ii + 3] = points_Left[v];
                
                // UV
                float[] uv_M = UV_Utility.GetQuadM(vertices[ii + 0], vertices[ii + 1], vertices[ii + 2], vertices[ii + 3]);
                
                float uv_Y_1_Pure = (float)(v + 1) / (count - 1);
                float uv_Y_2_Pure = (float)v / (count - 1);
                
                float uv_Y_1 = uvY + uv_Y_1_Pure / textureScale.y;
                float uv_Y_2 = uvY + uv_Y_2_Pure / textureScale.y;
                
                uv0[ii + 0] = new Vector3(uvX, uv_Y_1, 1) * uv_M[0]; // Stripes
                uv0[ii + 1] = new Vector3(uvX + 1 / textureScale.x, uv_Y_1, 1) * uv_M[1];
                uv0[ii + 2] = new Vector3(uvX + 1 / textureScale.x, uv_Y_2, 1) * uv_M[2];
                uv0[ii + 3] = new Vector3(uvX, uv_Y_2, 1) * uv_M[3];
                
                uv1[ii + 0] = new Vector3(0, uv_Y_1_Pure, 1) * uv_M[0]; // Gradient
                uv1[ii + 1] = new Vector3(1, uv_Y_1_Pure, 1) * uv_M[1];
                uv1[ii + 2] = new Vector3(1, uv_Y_2_Pure, 1) * uv_M[2];
                uv1[ii + 3] = new Vector3(0, uv_Y_2_Pure, 1) * uv_M[3];
                
                // Vertical colors
                colors[ii + 0] = colors[ii + 1] = color_V[v + 1];
                colors[ii + 2] = colors[ii + 3] = color_V[v];
                
                ii += 4;
            }
            
            // Indices
            int[] indices = new int[verticeCount];
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
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.RecalculateBounds();
        }
        
        
        [Serializable]
        public class Data
        {
            public AnimationCurve curve;
            [FormerlySerializedAs("curveScale")] public Vector2 widthCurve = Vector2.one;
            public float linearWidth;
            public float scale = 1;
            [Space]
            public Vector2 noiseScale;
            [Space]
            public Gradient gradient;
            [Space]
            public float opacityMin = 0.3f;
            public float opacityMax = 1f;
            [Space]
            public float timeFalloff = 1.05f;
            public float expansionFalloff = 1.2f;
            [Space]
            [Range(0, 1)] public float additiveBlend;
            [Space]
            public Vector2 textureScale = Vector2.one;
        }
    }
}
*/