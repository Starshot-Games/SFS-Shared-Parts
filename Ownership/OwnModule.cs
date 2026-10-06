using UnityEngine;

namespace SFS.Parts.Modules
{
    public class OwnModule : MonoBehaviour
    {
        public PartPack pack;
        
        // Owned
        public bool IsOwned
        {
            get
            {
                return true;
            }
        }
        
        // Premium
        public virtual bool IsPremium => true;
        
        public enum PartPack
        {
            BigParts = 0,
            Redstone_Atlas = 1,
            
            SaturnV = 2,
            
            Full_Version = 3, // For mac
        }
    }
    
    public enum OwnershipState
    {
        NotOwned,
        NotUnlocked,
        OwnedAndUnlocked,
    }
}