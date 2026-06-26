using UnityEngine;

namespace SFS.World.Drag
{
    public abstract class HeatModuleBase : MonoBehaviour
    {
        public Valid valid = new Valid();
        void OnEnable() => valid.valid = true;
        void OnDisable() => valid.valid = false;

        public abstract string Name { get; }
        public abstract bool IsHeatShield { get; }
        
        public abstract float Temperature { get; set; } // NaN when heat is 0
        public abstract int LastAppliedIndex { get; set; }
        public abstract float ExposedSurface { get; set; }
        
        public abstract float HeatTolerance { get; }
        public abstract void OnOverheat(bool breakup);
    }

    public class Valid
    {
        public bool valid;
    }
}