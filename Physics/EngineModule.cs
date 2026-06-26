using System.Collections.Generic;
using System.Linq;
using SFS.Builds;
using SFS.Translations;
using SFS.UI;
using SFS.Variables;
using SFS.World;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules 
{
    public partial class EngineModule : MonoBehaviour, Rocket.INJ_IsPlayer, Rocket.INJ_Physics, Rocket.INJ_Throttle, Rocket.INJ_TurnAxisTorque, Rocket.INJ_Rocket, Rocket.INJ_Location
    {
        [BoxGroup("Ref", false), SuffixLabel("t"), LabelText("Engine Mass (Scaled)")] public float oldMass = float.NaN;
        //
        [BoxGroup("Ref", false), SuffixLabel("t")] public Composed_Float thrust;
        [BoxGroup("Ref", false)] public Composed_Vector2 thrustNormal = new Composed_Vector2(Vector2.up);
        //
        [BoxGroup("Ref", false)] public Composed_Float ISP;
        [BoxGroup("Ref", false)] public Composed_Vector2 thrustPosition = new Composed_Vector2(Vector2.zero);
        [BoxGroup("Ref", false), Required] public FlowModule source;
        //
        [BoxGroup("Ref", false)] public bool hasGimbal = true;
        [BoxGroup("Ref", false), ShowIf("hasGimbal")] public Bool_Reference gimbalOn;
        [BoxGroup("Ref", false), ShowIf("hasGimbal")] public MoveModule gimbal;
        //
        [BoxGroup("State", false)] public Bool_Reference engineOn;
        [BoxGroup("State", false)] public Float_Reference throttle_Out;
        //
        [BoxGroup("Vacuum", false)] public bool isVacuum;
        public const double maxDensity = 0.000015;
        // Heat
        [Space, Space]
        public Bool_Reference heatOn;
        //
        public bool multipleNozzles;
        [HideIf(nameof(multipleNozzles)), Required] public GameObject heatHolder;
        [HideIf(nameof(multipleNozzles))] Vector3 originalPosition;
        //
        [ShowIf(nameof(multipleNozzles))] public GameObject[] heatHolders;
        [ShowIf(nameof(multipleNozzles))] Vector3[] originalPositions;

        
        // Data Injection
        public Rocket Rocket { get; set; }
        public Location Location { get; set; }
        public bool IsPlayer { get; set; }
        public Rigidbody2D Rb2d { get; set; }
        //
        readonly Float_Local throttle_Input = new Float_Local();
        readonly Float_Local turnAxis_Input = new Float_Local();
        float Rocket.INJ_Throttle.Throttle { set => throttle_Input.Value = value; }
        float Rocket.INJ_TurnAxisTorque.TurnAxis { set => turnAxis_Input.Value = value; }

        partial void Check(ref Vector2 force);

        
        // Get
        I_MsgLogger Logger => IsPlayer ? (I_MsgLogger)MsgDrawer.main : new MsgNone();
        bool HasFuel(I_MsgLogger logger) => source.CanFlow(logger);


        // Description
        public void Draw(List<EngineModule> modules, StatsMenu drawer, PartDrawSettings settings)
        {
            drawer.DrawStat(50, thrust.Value.ToThrustString());
            drawer.DrawStat(40, (ISP.Value * (float)Base.worldBase.settings.difficulty.IspMultiplier).ToEfficiencyString());
            
            if (settings.build || settings.game)
            {
                drawer.DrawToggle(0, () => Loc.main.Engine_On_Label, ToggleEngineOn, () => engineOn.Value, update => engineOn.OnChange += update, update => engineOn.OnChange -= update);
                
                if (hasGimbal)
                    drawer.DrawToggle(0, () => Loc.main.Gimbal_On_Label, ToggleGimbal, () => gimbalOn.Value, update => gimbalOn.OnChange += update, update => gimbalOn.OnChange -= update);
                
                void ToggleEngineOn()
                {
                    Undo.main.RecordStatChangeStep(modules, () =>
                    {
                        bool on = !engineOn.Value;
                        foreach (EngineModule module in modules)
                            module.engineOn.Value = on;
                    });
                }
                
                void ToggleGimbal()
                {
                    Undo.main.RecordStatChangeStep(modules, () =>
                    {
                        bool on = !gimbalOn.Value;
                        foreach (EngineModule module in modules)
                            if (hasGimbal)
                                module.gimbalOn.Value = on;
                    });
                }
            }
        }
        
        
        void Awake()
        {
            if (float.IsNaN(oldMass))
                return;
            
            double massMultiplier = Base.worldBase.insideWorld.Value? Base.worldBase.settings.difficulty.EngineMassMultiplier : 1;
            GetComponent<VariablesModule>().doubleVariables.SetValue("mass", oldMass * massMultiplier, (true, false));
        }
        void Start()
        {
            if (HomeManager.main != null)
            {
                enabled = false;
                return;
            }
            
            source.onStateChange += CheckOutOfFuel;
            
            thrust.OnChange += RecalculateMassFlow;
            ISP.OnChange += RecalculateMassFlow;
            throttle_Out.OnChange += RecalculateMassFlow;
            
            throttle_Out.OnChange += UpdateApplyPhysics;
            
            
            // Stops bp edit cheating
            if (GameManager.main != null)
                CheckOutOfFuel();
            
            
            engineOn.OnChange += RecalculateEngineThrottle;
            throttle_Input.OnChange += RecalculateEngineThrottle;

            if (hasGimbal)
            {
                throttle_Out.OnChange += RecalculateGimbal;
                turnAxis_Input.OnChange += RecalculateGimbal;
            }
            
            heatHolder.gameObject.SetActive(true);
            
            if (GameManager.main != null)
            {
                if (multipleNozzles)
                    originalPositions = heatHolders.Select(h => h.transform.localPosition).ToArray();
                else
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
        
        
        // On Change
        void RecalculateMassFlow()
        {
            float multiplier = transform.TransformVector(thrustNormal.Value).magnitude;
            source.SetMassFlow(thrust.Value * multiplier * throttle_Out.Value / (ISP.Value * (float)Base.worldBase.settings.difficulty.IspMultiplier));
        }
        void CheckOutOfFuel()
        {
            if (engineOn.Value && !HasFuel(Logger))
                engineOn.Value = false;
        }
        void RecalculateEngineThrottle()
        {
            throttle_Out.Value = engineOn.Value ? throttle_Input.Value : 0;
        }
        void RecalculateGimbal()
        {
            if (hasGimbal && gimbalOn.Value)
                gimbal.targetTime.Value = throttle_Out.Value > 0 ? turnAxis_Input.Value * transform.RotationDirection() : 0;
        }

        
        // Applies thrust
        void UpdateApplyPhysics()
        {
            enabled = Rb2d != null && throttle_Out.Value > 0;
        }
        void FixedUpdate()
        {
            if (Rb2d == null)
                return;
            if (isVacuum && engineOn.Value && throttle_Out.Value > 0 && !Location.planet.CanUseVacuumEngines(Location.Height))
            {
                engineOn.Value = false;
                if (IsPlayer)
                    Logger.Log(Loc.main.Cannot_Use_Vacuum_Engines_In_Atmosphere);
            }
            // Force
            Vector2 force_Local = thrustNormal.Value * (thrust.Value * 9.8f * throttle_Out.Value);
            Vector2 force = Base.worldBase.AllowsCheats? transform.TransformVector(force_Local) : transform.TransformVectorUnscaled(force_Local);
            Vector2 position = Rb2d.GetRelativePoint(Transform_Utility.LocalToLocalPoint(transform, Rb2d, thrustPosition.Value));

            Check(ref force);

            Rb2d.AddForceAtPosition(force, position, ForceMode2D.Force);
            
            // Heat
            PositionFlameHitbox();
        }
        void PositionFlameHitbox()
        {
            if (!Base.sceneLoader.isUnloading)
            {
                if (multipleNozzles)
                    for (int i = 0; i < heatHolders.Length; i++)
                    {
                        Transform h = heatHolders[i].transform;
                        h.localPosition = originalPositions[i] + h.parent.InverseTransformVector(Rb2d.linearVelocity * Time.fixedDeltaTime);
                    }
                else
                    heatHolder.transform.localPosition = originalPosition + heatHolder.transform.parent.InverseTransformVector(Rb2d.linearVelocity * Time.fixedDeltaTime);
            }
        }


        // Functions
        public void ToggleEngine()
        {
            if (engineOn.Value)
                DisableEngine(Logger);
            else
                EnableEngine(Logger);
        }
        void EnableEngine(I_MsgLogger logger)
        {
            if (!HasFuel(logger))
                return;

            if (isVacuum && !Location.planet.CanUseVacuumEngines(Location.Height))
            {
                string height = Location.planet.VacuumStartHeight.ToDistanceString(false);
                logger.Log(Loc.main.Cannot_Ignite_Vacuum_Engines_Below.Inject("height", height));
                return;
            }
            
            engineOn.Value = true;

            if (throttle_Out.Value == 0)
                logger.Log(Loc.main.Engine_Module_State.InjectField(engineOn.Value.State_ToOnOff(), "state"));
        }
        void DisableEngine(I_MsgLogger logger)
        {
            bool wasAlreadyNotBurning = throttle_Out.Value == 0;
            engineOn.Value = false;

            if (wasAlreadyNotBurning)
                logger.Log(Loc.main.Engine_Module_State.InjectField(engineOn.Value.State_ToOnOff(), "state"));
        }

    }
}