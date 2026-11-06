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
        
        public abstract float Isp { get; } // Scaled by world difficulty setting
        
        public abstract Float_Reference ThrottleOut { get; }
        public abstract Bool_Reference On { get; }

        public abstract Vector2 GetThrustForce(); // World space, throttle applied

        public Rocket Rocket { get; set; }
    }
}