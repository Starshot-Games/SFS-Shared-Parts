using System;
using SFS.Translations;
using SFS.Variables;
using SFS.World;
using SFS.World.Drag;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class HeatModule : HeatModuleBase, Rocket.INJ_Rocket
    {
        // Refs
        public HeatTolerance heatTolerance;
        public bool isHeatShield;
        [Space]
        public bool useCustomName;
        [ShowIf(nameof(useCustomName))] public TranslationVariable customName;
        
        // State
        [Space]
        [ShowInInspector] float temperature = float.NegativeInfinity;
        public HeatboxModule[] ignoredHeatboxes = Array.Empty<HeatboxModule>();
        
        
        Part part;
        void Start() => part = transform.GetComponentInParentTree<Part>();

        public Rocket Rocket { get; set; }
        
        // Implementation
        public override string Name
        {
            get
            {
                if (useCustomName)
                    return customName.Field;
                
                if (part == null)
                    part = transform.GetComponentInParentTree<Part>();
                    
                return part.GetDisplayName();
            }
        }
        public override bool IsHeatShield => isHeatShield;
        public override float Temperature { get => temperature; set => temperature = value; }
        public override int LastAppliedIndex { get; set; } = -1;
        public override float ExposedSurface { get; set; } = 0;
        public override HeatboxModule[] IgnoredHeatboxes => ignoredHeatboxes;
        public override float HeatTolerance => AeroModule.GetHeatTolerance(heatTolerance);
        public override void OnOverheat(bool breakup) => part.OnOverheat(this, breakup);
        public override void Heat()
        {
            Rocket.aero.heatManager.HeatPart(this);
        }
    }
}