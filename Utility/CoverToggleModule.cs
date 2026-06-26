using System;
using System.Collections.Generic;
using SFS.Builds;
using SFS.Translations;
using SFS.Variables;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    // Ported from SFS2's CoverToggleModule. Cycles a part through visual "modes" - each mode
    // activates a set of GameObjects and repositions some transforms.
    //
    // The SFS2 magnet / build-node / interstage repositioning has been intentionally dropped, so this
    // only handles the visual side (active state + local positions).
    public class CoverToggleModule : MonoBehaviour
    {
        public Mode[] modes;

        // State - persisted through the part's variables (bind a variable name in the inspector to save it)
        public Double_Reference modeIndex;

        int CurrentIndex => modes.Length == 0 ? 0 : Mathf.Clamp((int)modeIndex.Value, 0, modes.Length - 1);


        void Start()
        {
            // Re-apply the saved mode on load
            ApplyMode(CurrentIndex, false, false);
        }

        [Button(ButtonSizes.Medium)]
        public void CycleMode()
        {
            if (modes.Length == 0)
                return;

            int newIndex = (CurrentIndex + 1) % modes.Length;
            bool playing = Application.isPlaying;
            ApplyMode(newIndex, playing, playing);
        }

        // Part description - on/off toggle for two modes, a cycle button for more than two.
        public void Draw(List<CoverToggleModule> modules, StatsMenu drawer, PartDrawSettings settings)
        {
            if (modes.Length < 2 || !(settings.build || settings.game))
                return;

            if (modes.Length == 2)
                drawer.DrawToggle(0, () => Loc.main.Engine_Cover_Label, () => SetMode(modeIndex.Value > 0 ? 0 : 1), () => modeIndex.Value > 0,
                    update => modeIndex.OnChange += update, update => modeIndex.OnChange -= update);
            else
                drawer.DrawButton(0, () => Loc.main.Engine_Cover_Label, () => $"{CurrentIndex + 1}/{modes.Length}", () => SetMode((CurrentIndex + 1) % modes.Length), () => true,
                    update => modeIndex.OnChange += update, update => modeIndex.OnChange -= update);

            void SetMode(int newIndex)
            {
                Undo.main.RecordStatChangeStep(modules, () =>
                {
                    foreach (CoverToggleModule module in modules)
                        module.ApplyMode(newIndex, false, false);
                });
            }
        }

        public void ApplyMode(int newModeIndex, bool createUndo, bool createNewUndoStep)
        {
            if (modes.Length == 0)
                return;

            if (createUndo)
            {
                Part part = transform.GetComponentInParentTree<Part>();
                Undo.main.RecordStatChangeStep(new[] { part }, () => ApplyMode(newModeIndex, false, false), createNewUndoStep);
                return;
            }

            Mode oldMode = modes[CurrentIndex];
            Mode newMode = modes[newModeIndex];

            // Disable old transforms
            foreach (GameObject t in oldMode.transformActivates)
                t.SetActive(false);

            // Move transforms
            foreach (MoveTransform move in newMode.transformMoves)
                move.transform.localPosition = move.newPosition;

            // Activate new transforms
            foreach (GameObject t in newMode.transformActivates)
                t.SetActive(true);

            modeIndex.Value = newModeIndex;
        }


        [Serializable]
        public class Mode
        {
            [Title("---")]
            public GameObject[] transformActivates;
            public MoveTransform[] transformMoves;
        }
        [Serializable]
        public class MoveTransform
        {
            public Transform transform;
            public Vector3 newPosition;
        }
    }
}
