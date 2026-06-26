using System;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class FlameGlowModule : MonoBehaviour
    {
        [ReadOnly] public float lastApplied_Throttle;
        [ReadOnly] public float lastApplied_Vacuum;

        public bool machDiamonds;
        public bool Vac;

        // Data
        [HideInInspector] public SpriteRenderer[] glows;
        [HideIf(nameof(Vac))] public GlowsData groundData = new GlowsData();
        [HideIf(nameof(machDiamonds))] public GlowsData vacuumData = new GlowsData();

        // Recording (drive a full-throttle preview through EngineEffects' debug sliders, then record)
        [PropertySpace, Button(ButtonSizes.Medium), HorizontalGroup, EnableIf(nameof(CanRecordGround)), HideIf(nameof(Vac))] void RecordGround() => RecordData(groundData, 0);
        [PropertySpace, Button(ButtonSizes.Medium), HorizontalGroup, EnableIf(nameof(CanRecordVacuum)), HideIf(nameof(machDiamonds))] void RecordVacuum() => RecordData(vacuumData, 1);
        //
        bool CanRecordGround => CanRecord(0);
        bool CanRecordVacuum => CanRecord(1);
        //
        void RecordData(GlowsData a, float intendedVacuum)
        {
            if (CanRecord(intendedVacuum))
            {
                glows = GetComponentsInChildren<SpriteRenderer>();
                a.color = glows.Select(x => x.color).ToArray();
                a.position = glows.Select(x => (Vector2)x.transform.localPosition).ToArray();
                a.scale = glows.Select(x => (Vector2)x.transform.localScale).ToArray();
            }
            else
                throw new Exception("WARNING: in preview, not full throttle or non matching vacuum");
        }
        bool CanRecord(float intendedVacuum) => lastApplied_Throttle == 1 && lastApplied_Vacuum == intendedVacuum;


        // Driven by EngineEffects - it gathers throttle/vacuum once and pushes them in here.
        public void Apply(float throttle, float vacuum)
        {
            if (glows == null)
                return;

            if (Vac)
                vacuum = 1;

            int maxValid = Mathf.Min(glows.Length, Vac ? 1000 : groundData.color.Length, machDiamonds ? 1000 : vacuumData.color.Length);
            for (int i = 0; i < maxValid; i++)
            {
                SpriteRenderer a = glows[i];

                if (a == null)
                    continue;

                // Color
                Color color;
                if (machDiamonds)
                {
                    color = groundData.color[i];
                    color.a *= Mathf.InverseLerp(0.5f, 1, throttle) * Mathf.Lerp(1, 0, vacuum * 20);
                }
                else
                    color = Color.Lerp(groundData.color[i], vacuumData.color[i], vacuum);

                color.a *= throttle;
                a.color = color;

                // Position
                a.transform.localPosition = machDiamonds ? groundData.position[i] : Vector2.Lerp(groundData.position[i], vacuumData.position[i], vacuum);

                // Scale (hand it to FlameRandomizer if present, else set localScale directly)
                Vector2 scale = machDiamonds ? groundData.scale[i] : Vector2.Lerp(groundData.scale[i], vacuumData.scale[i], vacuum);
                //
                FlameRandomizer randomizer = a.GetComponent<FlameRandomizer>();
                if (randomizer != null)
                    randomizer.size = scale;
                else
                    a.transform.localScale = scale;
            }

            lastApplied_Throttle = throttle;
            lastApplied_Vacuum = vacuum;
        }


        [Serializable]
        public class GlowsData
        {
            public Color[] color = new Color[0];
            public Vector2[] position = new Vector2[0];
            public Vector2[] scale = new Vector2[0];
        }
    }
}
