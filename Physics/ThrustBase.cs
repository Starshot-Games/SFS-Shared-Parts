using SFS.Variables;
using SFS.World;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public abstract class ThrustBase : MonoBehaviour, Rocket.INJ_Rocket
    {
        // Here we simply expose common values through properties to avoid breaking all the prefabs in the editor.
        
        public abstract float ThrustAmount { get; }
        public abstract Vector2 ThrustNormal { get; }
        public abstract Vector2 ThrustPosition { get; }
        
        public abstract bool HeatOn { get; }
        public abstract GameObject HeatHolder { get; }
        public abstract GameObject HeatHitbox { get; }

        public abstract Float_Reference ThrottleOut { get; }
        public abstract Bool_Reference On { get; }
        
        public Rocket Rocket { get; set; }
    }
}