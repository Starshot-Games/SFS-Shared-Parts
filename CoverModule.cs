using System;
using System.Linq;
using SFS.Variables;
using SFS.Translations;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    [HideMonoScript]
    public class CoverModule : MonoBehaviour, I_InitializePartModule, I_PartMenu
    {
        [Required] public String_Reference coverVariable;
        [SerializeField] CoverType[] covers;
        
        int coverCounter = 0;

        string CurrentCoverName() => covers[coverCounter].displayName.Field;

        void NextCover()
        {
            coverCounter++;
            
            if (coverCounter >= covers.Length)
                coverCounter = 0;
            
            coverVariable.Value = covers[coverCounter].name;
        }

        void UpdateCover()
        {
            for (int i = 0; i < covers.Length; i++)
            {
                if (i == coverCounter)
                    foreach (Transform a in covers[i].cover)
                        a.gameObject.SetActive(true);
                else
                    foreach (Transform a in covers[i].cover)
                        a.gameObject.SetActive(false);
            }
        }

        #warning Create translation filed for cover title
        // Part menu draw
        void I_PartMenu.Draw(StatsMenu drawer, PartDrawSettings settings)
        {
            if (settings.build)
                drawer.DrawButton(-1, () => "Cover", CurrentCoverName, NextCover, () => true, null, null);
        }
        
        // Implementation
        int I_InitializePartModule.Priority => -1;
        void I_InitializePartModule.Initialize()
        {
            if (covers.Any(x => x.name == coverVariable.Value))
                coverCounter = Array.FindIndex(covers, x => x.name == coverVariable.Value);
            
            coverVariable.OnChange += UpdateCover;
            UpdateCover();
        }

        [Serializable]
        struct CoverType
        {
            public string name;
            public TranslationVariable displayName;
            public Transform[] cover;
        }
    }
}