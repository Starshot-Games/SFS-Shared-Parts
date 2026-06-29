using System;
using SFS.UI;
using SFS.Variables;
using SFS.World;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using SFS.Builds;
using SFS.Translations;
using UnityEngine;

namespace SFS.Parts.Modules
{
    public class BoosterModule : MonoBehaviour, Rocket.INJ_Rocket, I_InitializePartModule, ResourceDrawer.I_Resource, Rocket.INJ_Throttle
    {
        public SurfaceData surfaceForCover;
        [Space] [Required] public ResourceType resourceType;
        public Composed_Float ISP;
        public Composed_Vector2 thrustVector;
        public Composed_Vector2 thrustPosition;

        [BoxGroup] public Composed_Float wetMass;
        [BoxGroup] public Composed_Float dryMassPercent;
        [BoxGroup] public Double_Reference fuelPercent;
        //
        public Bool_Reference boosterPrimed;
        public Bool_Reference boosterOn;
        //
        [BoxGroup("Output", false)] public Float_Reference throttle_Out;
        [BoxGroup("Output", false)] public Double_Reference mass_Out = new();
        [BoxGroup("Output", false)] public bool setDensity = true;

        // Patch
        [Required] public GameObject heatHolder;
        [Required] public GameObject heatHitbox;
        Vector3 originalPosition;


        // Injected
        public Rocket Rocket { get; set; }
        Float_Local throttle_Input = new Float_Local();
        public float Throttle { set => throttle_Input.Value = value; }
        // Ref
        Part part;

        // Get
        double BurnTimeLeft => TotalBurnTime * fuelPercent.Value;
        double FuelMass => TotalFuelCapacity * fuelPercent.Value;
        double TotalBurnTime => TotalFuelCapacity / (thrustVector.Value.magnitude / (ISP.Value * (float)Base.worldBase.settings.difficulty.IspMultiplier));
        double TotalFuelCapacity => (1 - DryMassPercent) * wetMass.Value;
        double DryMassPercent => dryMassPercent.Value * (float)Base.worldBase.settings.difficulty.DryMassMultiplier;


        // Description
        [Space]
        public bool showDescription = true;
        public void Draw(List<BoosterModule> modules, StatsMenu drawer, PartDrawSettings settings)
        {
            if (!showDescription)
                return;

            // Thrust, burn time, isp
            drawer.DrawStat(53, thrustVector.Value.magnitude.ToThrustString());
            drawer.DrawStat(52, ISP.Value.ToEfficiencyString());
            drawer.DrawSpace(51);
            drawer.DrawStat(50, () => BurnTimeLeft.ToBurnTimeString(false), () => BurnTimeLeft.ToBurnTimeString(true), Register, Unregister);

            // Fuel slider/value
            if (settings.build || settings.game)
                drawer.DrawSlider(0, GetLabelAndValue, MaxSize, () => (float)fuelPercent.Value, SetResource, Register, Unregister); // Slider
            else
                drawer.DrawStat(0, GetLabelAndValue, MaxSize, Register, Unregister); // Stat

            string GetLabelAndValue() => Get(FuelMass);
            string MaxSize() => Get(TotalFuelCapacity);
            string Get(double fuelPercent) => Loc.main.Info_Resource_Amount.InjectField(resourceType.displayName, "resource").Inject((fuelPercent).ToString(2, false) + resourceType.resourceUnit.Field, "amount");

            void Register(Action update) => fuelPercent.OnChange += update;
            void Unregister(Action update) => fuelPercent.OnChange -= update;

            void SetResource(float newValue, bool touchStart)
            {
                Undo.main.RecordStatChangeStep(modules, () =>
                {
                    if (BuildManager.main != null)
                        foreach (BoosterModule module in modules)
                            module.fuelPercent.Value = newValue;

                }, touchStart);
            }
        }


        // Setup
        int I_InitializePartModule.Priority => -1;

        void I_InitializePartModule.Initialize()
        {
            part = transform.GetComponentInParentTree<Part>();
            enabled = boosterOn.Value;

            enabled = fuelPercent.Value is < 1.0f and > 0.0f;

            throttle_Input.OnChange += TryIgnite;

            wetMass.OnChange += RecalculateMass;
            dryMassPercent.OnChange += RecalculateMass;
            fuelPercent.OnChange += RecalculateMass;

            throttle_Out.OnChange += () =>
            {
                heatHolder.SetActive(throttle_Out.Value > 0);
                heatHitbox.transform.localScale = new Vector3(1, throttle_Out.Value, 1);
            };
        }

        void RecalculateMass()
        {
            mass_Out.Value = DryMassPercent * wetMass.Value + FuelMass;

            if (setDensity)
                part.density.Value = Mathf.Lerp((float)DryMassPercent, 1, (float)fuelPercent.Value) * resourceType.density;
        }


        void Start()
        {
            if (GameManager.main != null)
            {
                originalPosition = heatHolder.transform.localPosition;
                WorldView.main.onVelocityOffset += PositionFlameHitbox;
            }
        }

        void OnDestroy()
        {
            if (GameManager.main != null)
                WorldView.main.onVelocityOffset -= PositionFlameHitbox;
        }

        void PositionFlameHitbox(Vector2 _) => PositionFlameHitbox();
        void PositionFlameHitbox()
        {
            // Physics simulation is one frame behind, this fixes it
            heatHolder.transform.localPosition = originalPosition + heatHolder.transform.parent.InverseTransformVector(Rocket.rb2d.linearVelocity * Time.fixedDeltaTime);
        }


        // Activation
        public void Fire()
        {
            // Check if can use booster
            if (!CanUseBooster(this, Rocket.isPlayer.Value))
                return;

            boosterPrimed.Value = !boosterPrimed.Value;
            TryIgnite();

            if (!enabled)
                MsgDrawer.main.Log(boosterPrimed.Value ? Loc.main.Booster_On : Loc.main.Booster_Off);

            // Old code from career attempt...
            /*
            // Regular
            if (CareerState.main.HasFeature(WorldSave.CareerState.throttleFeature))
            {
                boosterPrimed.Value = !boosterPrimed.Value;
                TryIgnite();

                if (!enabled)
                    MsgDrawer.main.Log(boosterPrimed.Value? Loc.main.Booster_On : Loc.main.Booster_Off);

                return;
            }

            // For career
            BoosterModule[] usableBoosters = transform.GetComponentInParentTree<Rocket>().partHolder.GetModules<BoosterModule>().Where(b => CanUseBooster(b, false)).ToArray();
            if (usableBoosters.Length == 1)
            {
                // Ignites instantly
                Ignite();
            }
            else
            {
                // Syncs ignition
                newIgnitionTime = Time.unscaledTime + 0.75f;
                usableBoosters.Where(b => b.newIgnitionTime != -1).ForEach(b => b.newIgnitionTime = newIgnitionTime);
                StartCoroutine(FireDelayed());

                IEnumerator FireDelayed()
                {
                    while (Time.unscaledTime < newIgnitionTime)
                        yield return new WaitForFixedUpdate();

                    Ignite();
                }
            }
            ,
            boosterPrimed.Value = !boosterPrimed.Value;
            TryIgnite();

            if (!enabled)
                MsgDrawer.main.Log("Booster " + (boosterPrimed.Value? "On" : "Off"));*/

        }

        void TryIgnite()
        {
            if (boosterPrimed.Value && throttle_Input.Value > 0.1f && CanUseBooster(this, false))
                Ignite();
        }

        void Ignite()
        {
            boosterPrimed.Value = false;
            throttle_Out.Value = 1;
            boosterOn.Value = true;
            enabled = true;
        }

        bool CanUseBooster(BoosterModule boosterModule, bool showMsg)
        {
            if (surfaceForCover != null && SurfaceData.IsSurfaceCovered(surfaceForCover))
            {
                if (showMsg)
                    MsgDrawer.main.Log(Loc.main.Cannot_Ignite_Covered_Booster);

                return false;
            }

            if (boosterModule.enabled)
            {
                if (showMsg)
                    MsgDrawer.main.Log(Loc.main.Booster_Cannot_Be_Off);

                return false;
            }

            if (!SandboxSettings.main.settings.infiniteFuel && boosterModule.fuelPercent.Value == 0)
            {
                if (showMsg)
                    MsgDrawer.main.Log(Loc.main.Msg_No_Resource_Left.InjectField(resourceType.displayName, "resource", true));

                return false;
            }

            return true;
        }


        // Thrust
        void FixedUpdate()
        {
            if (Rocket == null)
                return;

            // Removes fuel
            if (!SandboxSettings.main.settings.infiniteFuel)
            {
                fuelPercent.Value -= Time.fixedDeltaTime / TotalBurnTime * throttle_Out.Value;

                if (fuelPercent.Value <= 0)
                {
                    // Out of fuel
                    throttle_Out.Value = 0;
                    fuelPercent.Value = 0;
                    boosterOn.Value = false;
                    enabled = false;

                    if (Rocket.isPlayer.Value)
                        MsgDrawer.main.Log(Loc.main.Msg_No_Resource_Left.InjectField(resourceType.displayName, "resource", true));

                    return;
                }
            }

            // Force
            Rigidbody2D rb2d = Rocket.rb2d;
            Vector2 force_Local = thrustVector.Value * 9.8f;
            Vector2 force = Base.worldBase.AllowsCheats ? transform.TransformVector(force_Local) : transform.TransformVectorUnscaled(force_Local);
            Vector2 position = rb2d.GetRelativePoint(Transform_Utility.LocalToLocalPoint(transform, rb2d, thrustPosition.Value));
            rb2d.AddForceAtPosition(force, position, ForceMode2D.Force);

            // Heat
            PositionFlameHitbox();
        }

        // Implementation
        ResourceType ResourceDrawer.I_Resource.ResourceType => resourceType;
        float ResourceDrawer.I_Resource.WetMass => wetMass.Value;
        Double_Reference ResourceDrawer.I_Resource.ResourcePercent => fuelPercent;
    }
}