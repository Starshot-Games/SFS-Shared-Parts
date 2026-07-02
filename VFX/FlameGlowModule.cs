using System;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class FlameGlowModule : MonoBehaviour
    {
        public bool vac;

        // Debug preview - refreshes on validate. EngineEffects drives this at runtime.
        #if UNITY_EDITOR
        [BoxGroup("Debug"), Range(0, 1)] public float debugThrottle, debugVacuum;
        #endif

        // Data
        [Space]
        [ReadOnly] public SpriteRenderer[] glows;
        [HideIf(nameof(vac))] public GlowsData groundData = new GlowsData();
        public GlowsData vacuumData = new GlowsData();
        
        [Space]
        [ReadOnly] public float lastApplied_Throttle;
        [ReadOnly] public float lastApplied_Vacuum;

        // Recording (drive a full-throttle preview through EngineEffects' debug sliders, then record)
        [PropertySpace, Button(ButtonSizes.Medium), HorizontalGroup, EnableIf(nameof(CanRecordGround)), HideIf(nameof(vac))] void RecordGround() => RecordData(groundData, 0);
        [PropertySpace, Button(ButtonSizes.Medium), HorizontalGroup, EnableIf(nameof(CanRecordVacuum))] void RecordVacuum() => RecordData(vacuumData, 1);
        //
        bool CanRecordGround => CanRecord(0);
        bool CanRecordVacuum => CanRecord(1);
        //
        void RecordData(GlowsData a, float intendedVacuum)
        {
            if (!CanRecord(intendedVacuum))
                throw new Exception("WARNING: in preview, not full throttle or non matching vacuum");
            
            glows = GetComponentsInChildren<SpriteRenderer>();
            a.color = glows.Select(x => x.color).ToArray();
            a.position = glows.Select(x => (Vector2)x.transform.localPosition).ToArray();
            a.scale = glows.Select(x => (Vector2)x.transform.localScale).ToArray();
        }
        bool CanRecord(float intendedVacuum) => lastApplied_Throttle == 1 && lastApplied_Vacuum == intendedVacuum;


        // Driven by EngineEffects - it gathers throttle/vacuum once and pushes them in here.
        public void Apply(float throttle, float vacuum)
        {
            if (glows == null)
                return;

            // Fully disable the glow objects when the engine is off, rather than only fading their alpha.
            bool on = throttle > 0;
            foreach (SpriteRenderer glow in glows)
                if (glow != null && glow.gameObject.activeSelf != on)
                    glow.gameObject.SetActive(on);

            if (vac)
                vacuum = 1;

            if (on)
            {
                int maxValid = Mathf.Min(glows.Length, vac ? 1000 : groundData.color.Length, vacuumData.color.Length);
                for (int i = 0; i < maxValid; i++)
                {
                    SpriteRenderer a = glows[i];

                    if (a == null)
                        continue;

                    // Color
                    Color color = Color.Lerp(groundData.color[i], vacuumData.color[i], vacuum);

                    color.a *= throttle;
                    a.color = color;

                    // Position
                    a.transform.localPosition = Vector2.Lerp(groundData.position[i], vacuumData.position[i], vacuum);

                    // Scale (hand it to FlameRandomizer if present, else set localScale directly)
                    Vector2 scale = Vector2.Lerp(groundData.scale[i], vacuumData.scale[i], vacuum);
                    //
                    FlameRandomizer randomizer = a.GetComponent<FlameRandomizer>();
                    if (randomizer != null)
                        randomizer.size = scale;
                    else
                        a.transform.localScale = scale;
                }
            }

            lastApplied_Throttle = throttle;
            lastApplied_Vacuum = vacuum;
        }

        #if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying && glows != null)
                Apply(debugThrottle, debugVacuum);
        }
        #endif


        [Serializable]
        public class GlowsData
        {
            public Color[] color = new Color[0];
            public Vector2[] position = new Vector2[0];
            public Vector2[] scale = new Vector2[0];
        }
    }
}
