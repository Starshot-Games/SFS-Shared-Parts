using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class FlameMeshModule : MonoBehaviour
    {
        static readonly int AdditiveBlend = Shader.PropertyToID("_AdditiveBlend");
        static readonly int Throttle = Shader.PropertyToID("_Throttle");
        static readonly int Vacuum = Shader.PropertyToID("_Vacuum");
        static readonly int Size = Shader.PropertyToID("_Size");
        static readonly int StripesWidth = Shader.PropertyToID("_StripesWidth");
        static readonly int StripesStrength = Shader.PropertyToID("_StripesStrength");
        
        
        // Debug preview - refreshes on validate. EngineEffects drives this at runtime.
        #if UNITY_EDITOR
        [BoxGroup("Debug"), Range(0, 1)] public float debugThrottle, debugVacuum;
        #endif

        public float combustionChamberDensity = 10;
        public float expansionRatio = 20;
        public float size = 1;
        [Space]
        public float stripesWidth = 1;
        [Range(0, 3)] public float stripesStrength = 1;

        [ReadOnly, ShowInInspector] float exitPressure;
        void OnValidate()
        {
            exitPressure = combustionChamberDensity / expansionRatio;

            #if UNITY_EDITOR
            if (!Application.isPlaying && meshRenderers != null)
                Apply(debugThrottle, debugVacuum);
            #endif
        }
        
        public bool isVacuumEngine;


        // Ref
        [Space]
        [Space]
        public MeshRef[] meshRenderers;

        [Space]
        public Data groundData;
        [Space]
        public Data vacuumData;


        #if UNITY_EDITOR
        // Flat grid plane (xVertices * yVertices). Width 1 (x: -0.5..0.5), height 0..-1.
        [PropertySpace, Button(ButtonSizes.Medium, Name = "Create 2D Mesh Asset")]
        void Create2DMesh(int xVertices = 2, int yVertices = 32)
        {
            string name = $"Flame Flat {xVertices}x{yVertices}";
            Mesh mesh = new Mesh { name = name };
            BuildGrid(mesh, xVertices, yVertices, false);
            SaveMeshAsset(mesh, name);
        }

        // Cylinder (sides around, yVertices tall). Width 1 (radius 0.5), height 0..-1.
        [Button(ButtonSizes.Medium, Name = "Create 3D Mesh Asset")]
        void Create3DMesh(int sides = 12, int yVertices = 32, bool capTop = false)
        {
            string name = $"Flame Cylinder {sides}x{yVertices}" + (capTop ? " Capped" : "");
            Mesh mesh = new Mesh { name = name };
            BuildGrid(mesh, sides + 1, yVertices, true, capTop); // +1 so the seam UV wraps cleanly
            SaveMeshAsset(mesh, name);
        }

        // Copies a mesh (e.g. the mach diamonds mesh) with its UV0 and UV1 channels swapped
        [Button(ButtonSizes.Medium, Name = "Copy Mesh (Swap UV0/UV1)")]
        void CopyMeshSwapUVs(Mesh source)
        {
            if (source == null)
                return;

            Mesh copy = Instantiate(source);
            copy.name = source.name + " UVSwap";

            Vector2[] uv0 = source.uv;  // TEXCOORD0
            Vector2[] uv1 = source.uv2; // TEXCOORD1

            // Flip Y of what becomes UV0
            for (int i = 0; i < uv1.Length; i++)
                uv1[i].y = 1 - uv1[i].y;

            copy.uv = uv1;  // UV0 = old UV1, Y-flipped
            copy.uv2 = uv0; // UV1 = old UV0

            SaveMeshAsset(copy, copy.name);
        }

        // Flat (2D) version of the stacked-diamond mesh: diamondCount rhombi down y 0..-1, single sheet (z = 0).
        // Each diamond = top tip (x=0) -> wide middle (x: -0.5..0.5) -> bottom tip (x=0), matching the 3D bipyramid layout.
        // uv0 = (across, global height), uv1 = (across, local per-diamond: top 1 / mid 0.5 / bottom 0).
        [Button(ButtonSizes.Medium, Name = "Create 2D Diamond Mesh")]
        void Create2DDiamondMesh(int diamondCount = 5, int columns = 10)
        {
            int cols = Mathf.Max(columns, 2);
            int vertsPerDiamond = 3 * cols; // top tip, middle, bottom tip
            int vCount = vertsPerDiamond * diamondCount;

            Vector3[] vertices = new Vector3[vCount];
            Vector2[] uv0 = new Vector2[vCount];
            Vector2[] uv1 = new Vector2[vCount];

            float half = 1f / (4 * diamondCount); // half-height of each diamond

            for (int d = 0; d < diamondCount; d++)
            {
                float midY = -(2 * d + 1) / (2f * diamondCount);
                float topY = midY + half;
                float botY = midY - half;
                int baseV = d * vertsPerDiamond;

                for (int i = 0; i < cols; i++)
                {
                    float uT = (float)i / (cols - 1);        // 0..1 across the width
                    float midX = Mathf.Lerp(-0.5f, 0.5f, uT);

                    int top = baseV + i, mid = top + cols, bot = mid + cols;

                    vertices[top] = new Vector3(0, topY, 0);
                    vertices[mid] = new Vector3(midX, midY, 0);
                    vertices[bot] = new Vector3(0, botY, 0);

                    uv0[top] = new Vector2(uT, -topY); uv1[top] = new Vector2(uT, 1f);
                    uv0[mid] = new Vector2(uT, -midY); uv1[mid] = new Vector2(uT, 0.5f);
                    uv0[bot] = new Vector2(uT, -botY); uv1[bot] = new Vector2(uT, 0f);
                }
            }

            int[] triangles = new int[diamondCount * (cols - 1) * 2 * 6]; // two quad strips per diamond
            int t = 0;
            for (int d = 0; d < diamondCount; d++)
            {
                int top = d * vertsPerDiamond, mid = top + cols, bot = mid + cols;
                for (int i = 0; i < cols - 1; i++)
                {
                    // top -> middle
                    triangles[t++] = top + i; triangles[t++] = mid + i;     triangles[t++] = mid + i + 1;
                    triangles[t++] = top + i; triangles[t++] = mid + i + 1; triangles[t++] = top + i + 1;
                    // middle -> bottom
                    triangles[t++] = mid + i; triangles[t++] = bot + i;     triangles[t++] = bot + i + 1;
                    triangles[t++] = mid + i; triangles[t++] = bot + i + 1; triangles[t++] = mid + i + 1;
                }
            }

            Mesh mesh = new Mesh { name = $"Diamond Flat {diamondCount}x{cols}" };
            mesh.vertices = vertices;
            mesh.uv = uv0;  // TEXCOORD0
            mesh.uv2 = uv1; // TEXCOORD1
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMeshAsset(mesh, mesh.name);
        }

        // Builds a width-1, height 0..-1 grid. UV goes 0..1 with the top at 0 and the bottom at 1.
        static void BuildGrid(Mesh mesh, int xCount, int yCount, bool cylinder, bool capTop = false)
        {
            bool cap = cylinder && capTop;
            int gridVerts = xCount * yCount;

            Vector3[] vertices = new Vector3[gridVerts + (cap ? 1 : 0)];
            Vector2[] uv = new Vector2[gridVerts + (cap ? 1 : 0)];

            for (int y = 0; y < yCount; y++)
            {
                float vT = yCount > 1 ? (float)y / (yCount - 1) : 0; // 0 top -> 1 bottom
                for (int x = 0; x < xCount; x++)
                {
                    float uT = xCount > 1 ? (float)x / (xCount - 1) : 0; // 0 -> 1
                    int i = y * xCount + x;

                    if (cylinder)
                    {
                        float angle = uT * Mathf.PI * 2;
                        vertices[i] = new Vector3(Mathf.Sin(angle) * 0.5f, -vT, Mathf.Cos(angle) * 0.5f);
                    }
                    else
                        vertices[i] = new Vector3(uT - 0.5f, -vT, 0);

                    uv[i] = new Vector2(uT, vT);
                }
            }

            // Top cap centre vertex (at the nozzle, y = 0)
            int capCenter = gridVerts;
            if (cap)
            {
                vertices[capCenter] = new Vector3(0, 0, 0);
                uv[capCenter] = new Vector2(0.5f, 0);
            }

            int[] triangles = new int[(xCount - 1) * (yCount - 1) * 6 + (cap ? (xCount - 1) * 3 : 0)];
            int t = 0;
            for (int y = 0; y < yCount - 1; y++)
                for (int x = 0; x < xCount - 1; x++)
                {
                    int i = y * xCount + x;
                    int down = i + xCount;

                    triangles[t++] = i;
                    triangles[t++] = down;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = down;
                    triangles[t++] = down + 1;
                }

            // Top cap: fan from the centre to the top ring (row 0, at y = 0)
            if (cap)
                for (int x = 0; x < xCount - 1; x++)
                {
                    triangles[t++] = capCenter;
                    triangles[t++] = x + 1;
                    triangles[t++] = x;
                }

            mesh.vertices = vertices;
            mesh.uv = uv;  // TEXCOORD0 (UVQ_0)
            mesh.uv2 = uv; // TEXCOORD1 (UVQ_1)
            mesh.triangles = triangles;
            mesh.RecalculateNormals();

            // Weld normals across the cylinder seam: first/last column share a position but RecalculateNormals
            // only gives each the faces on its own side, leaving a lighting crease. Average them so it's smooth.
            if (cylinder)
            {
                Vector3[] normals = mesh.normals;
                for (int y = 0; y < yCount; y++)
                {
                    int first = y * xCount;
                    int last = first + xCount - 1;
                    Vector3 n = (normals[first] + normals[last]).normalized;
                    normals[first] = normals[last] = n;
                }
                mesh.normals = normals;
            }

            mesh.RecalculateBounds();
        }

        static void SaveMeshAsset(Mesh mesh, string name)
        {
            const string folder = "Assets/FlameStuff/Meshes";
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
                UnityEditor.AssetDatabase.CreateFolder("Assets/FlameStuff", "Meshes");

            string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset");
            UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.EditorGUIUtility.PingObject(mesh);
            Debug.Log("FlameMeshModule: created mesh asset at " + path);
        }
        #endif


        // Driven by EngineEffects - it gathers throttle/vacuum once and pushes them in here.
        public void Apply(float throttle, float vacuum)
        {
            // Set
            foreach (MeshRef a in meshRenderers)
            {
                MaterialPropertyBlock propertyBlock = new();

                propertyBlock.SetFloat(Throttle, throttle * Mathf.Lerp(1, 0.75f, vacuum));
                propertyBlock.SetFloat(Vacuum, vacuum);

                propertyBlock.SetFloat(AdditiveBlend, Mathf.Lerp(groundData.additiveBlend, vacuumData.additiveBlend, vacuum));

                propertyBlock.SetFloat("combustionChamberDensity", combustionChamberDensity);
                propertyBlock.SetFloat("expansionRatio", expansionRatio);

                propertyBlock.SetFloat("atmospherePressure", 1 - vacuum); // 1 atm at ground -> 0 in vacuum (driven by EngineEffects vac)

                propertyBlock.SetFloat("isMachDiamondsShader", a.machDiamondsMesh? 1 : 0);
                propertyBlock.SetFloat("isVacuumEngine", isVacuumEngine? 1 : 0);

                propertyBlock.SetFloat(Size, size);
                propertyBlock.SetFloat(StripesWidth, stripesWidth);
                propertyBlock.SetFloat(StripesStrength, stripesStrength);

                a.meshRenderer.SetPropertyBlock(propertyBlock, 0);
                a.meshRenderer.SetPropertyBlock(propertyBlock, 1);
            }
        }
        
        [Serializable]
        public class Data
        {
            [Range(0, 1)] public float additiveBlend;
        }
        
        [Serializable]
        public class MeshRef
        {
            public MeshRenderer meshRenderer;
            public bool machDiamondsMesh;
        }
    }
}