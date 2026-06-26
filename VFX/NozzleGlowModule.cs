using System;
using SFS.Variables;
using SFS.World;
using Sirenix.OdinInspector;
using UnityEngine;

[HideMonoScript]
public class NozzleGlowModule : MonoBehaviour
{
    static readonly int GlowIntensity = Shader.PropertyToID("_GlowIntensity");


    [Range(0, 2)] public float testGlowIntensity;
    [Space]
    public Float_Reference engineThrottle;
    public Float_Reference glowIntensity;
    public float heatUpTime = 25, coolDownTime = 75;
    [Space]
    public GlowMesh[] meshes;


    void Start()
    {
        engineThrottle.OnChange += UpdateEnabled;
        glowIntensity.OnChange += UpdateEnabled;
    }
    void UpdateEnabled()
    {
        bool enable = GameManager.main != null && glowIntensity.Value != engineThrottle.Value;

        if (enable != enabled)
            enabled = enable;
    }
    void Update()
    {
        float delta = 1 / (engineThrottle.Value > glowIntensity.Value? heatUpTime : coolDownTime) * WorldTime.DeltaTime;
        glowIntensity.Value = Mathf.MoveTowards(glowIntensity.Value, engineThrottle.Value, delta);
        SetGlow();
    }

    void OnValidate() => SetGlow();
    void SetGlow()
    {
        float intensity = glowIntensity.Value;

        #if UNITY_EDITOR
        if (!Application.isPlaying)
            intensity = testGlowIntensity;
        #endif

        intensity = Mathf.Pow(intensity, 2.2f);
        foreach (GlowMesh mesh in meshes)
        {
            if (mesh == null || mesh.mesh == null)
            {
                Debug.LogError("GLOW MESH NULL");
                continue;
            }
            
            
            MeshRenderer meshRenderer = mesh.mesh;
            const int Index = 0; // Won't work with submeshes, ok with it for now

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            if (meshRenderer.HasPropertyBlock())
                meshRenderer.GetPropertyBlock(propertyBlock, Index);

            propertyBlock.SetFloat(GlowIntensity, intensity * mesh.intensityMultiplier);
            meshRenderer.SetPropertyBlock(propertyBlock, Index);
        }
    }

    [Serializable]
    public class GlowMesh
    {
        public MeshRenderer mesh;
        public float intensityMultiplier = 1;
    }
}