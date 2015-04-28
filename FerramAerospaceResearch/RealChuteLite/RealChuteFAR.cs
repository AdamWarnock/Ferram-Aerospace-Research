﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Random = System.Random;

namespace FerramAerospaceResearch.RealChuteLite
{
    /// <summary>
    /// Parachute deployment states
    /// </summary>
    public enum DeploymentStates
    {
        NONE,
        STOWED,
        PREDEPLOYED,
        DEPLOYED,
        CUT
    }

    public class RealChuteFAR : PartModule, IModuleInfo
    {
        #region Constants
        //Few useful constants
        public const float areaDensity = 0.000058f, areaCost = 0.075f;
        public const int maxSpares = 5;
        public const string stowed = "STOWED", predeployed = "PREDEPLOYED", deployed = "DEPLOYED", cut = "CUT";

        //Quick enum parsing/tostring dictionaries
        private static readonly Dictionary<DeploymentStates, string> names = new Dictionary<DeploymentStates, string>(5)
        #region Names
        {
            { DeploymentStates.NONE, string.Empty },
            { DeploymentStates.STOWED, stowed },
            { DeploymentStates.PREDEPLOYED, predeployed },
            { DeploymentStates.DEPLOYED, deployed },
            { DeploymentStates.CUT, cut }
        };
        #endregion
        private static readonly Dictionary<string, DeploymentStates> states = new Dictionary<string, DeploymentStates>(5)
        #region States
        {
            { string.Empty, DeploymentStates.NONE },
            { stowed, DeploymentStates.STOWED },
            { predeployed, DeploymentStates.PREDEPLOYED },
            { deployed, DeploymentStates.DEPLOYED },
            { cut, DeploymentStates.CUT }
        };
        #endregion
        #endregion

        #region KSPFields
        //Stealing values from the stock module
        [KSPField]
        public float autoCutSpeed = 0.5f;
        [KSPField(guiName = "Min pressure", isPersistant = true, guiActive = true, guiActiveEditor = true), UI_FloatRange(stepIncrement = 0.01f, maxValue = 0.5f, minValue = 0.01f)]
        public float minAirPressureToOpen = 0.01f;
        [KSPField(guiName = "Altitude", isPersistant = true, guiActive = true, guiActiveEditor = true), UI_FloatRange(stepIncrement = 50f, maxValue = 5000f, minValue = 50f)]
        public float deployAltitude = 700;
        [KSPField]
        public string capName = "cap", canopyName = "canopy";
        [KSPField]
        public string semiDeployedAnimation = "semiDeploy", fullyDeployedAnimation = "fullyDeploy";
        [KSPField]
        public float semiDeploymentSpeed = 0.5f, deploymentSpeed = 0.16667f;
        [KSPField]
        public bool invertCanopy = true;

        //Persistant fields
        [KSPField(isPersistant = true)]
        public float preDeployedDiameter = 1, deployedDiameter = 25;
        [KSPField(isPersistant = true)]
        public float caseMass = 0, time = 0;
        [KSPField(isPersistant = true)]
        public bool armed = false, staged = false, initiated = false;
        [KSPField(isPersistant = true, guiActive = true, guiName = "Spare chutes")]
        public int chuteCount = 5;
        [KSPField(isPersistant = true)]
        public string depState = "STOWED";
        #endregion

        #region Propreties
        // If the vessel is stopped on the ground
        public bool groundStop
        {
            get { return this.vessel.LandedOrSplashed && this.vessel.horizontalSrfSpeed < this.autoCutSpeed; }
        }

        // If the parachute can be repacked
        public bool canRepack
        {
            get
            {
                return (this.groundStop || this.atmPressure == 0) && deploymentState == DeploymentStates.CUT
                    && this.chuteCount > 0 && FlightGlobals.ActiveVessel.isEVA;
            }
        }

        //If the Kerbal can repack the chute in career mode
        public bool canRepackCareer
        {
            get
            {
                ProtoCrewMember kerbal = FlightGlobals.ActiveVessel.GetVesselCrew()[0];
                return HighLogic.CurrentGame.Mode != Game.Modes.CAREER
                    || (kerbal.experienceTrait.Title == "Engineer" && kerbal.experienceLevel >= 1);
            }
        }

        //Predeployed area of the chute
        public float preDeployedArea
        {
            get { return GetArea(this.preDeployedDiameter); }
        }

        //Deployed area of the chute
        public float deployedArea
        {
            get { return GetArea(this.deployedDiameter); }
        }

        //Mass of the chute
        public float chuteMass
        {
            get { return this.deployedArea * areaDensity; }
        }

        public float totalMass
        {
            get
            {
                if (this.caseMass == 0) { this.caseMass = this.part.mass; }
                return this.caseMass + this.chuteMass;
            }
        }

        //Position to apply the force to
        public Vector3 forcePosition
        {
            get { return this.parachute.position; }
        }

        //If the random deployment timer has been spent
        public bool randomDeployment
        {
            get
            {
                if (!this.randomTimer.isRunning) { this.randomTimer.Start(); }

                if (this.randomTimer.elapsed.TotalSeconds >= this.randomTime)
                {
                    this.randomTimer.Reset();
                    return true;
                }
                return false;
            }
        }

        //If the parachute has passed the minimum deployment clause
        public bool deploymentClause
        {
            get
            {
                return this.atmPressure >= this.minAirPressureToOpen;
            }
        }

        //If the parachute can deploy
        public bool canDeploy
        {
            get
            {
                if (this.groundStop || this.atmPressure == 0) { return false; }
                else if (this.deploymentState == DeploymentStates.CUT) { return false; }
                else if (this.deploymentClause) { return true; }
                else if (!this.deploymentClause && this.isDeployed) { return true; }
                return false;
            }
        }

        //If the parachute is deployed
        public bool isDeployed
        {
            get
            {
                switch (this.deploymentState)
                {
                    case DeploymentStates.PREDEPLOYED:
                    case DeploymentStates.DEPLOYED:
                        return true;
                }
                return false;
            }
        }

        //Persistent deployment state
        public DeploymentStates deploymentState
        {
            get
            {
                if (this.state == DeploymentStates.NONE) { this.deploymentState = states[this.depState]; }
                    return state;
            }
            set
            {
                this.state = value;
                this.depState = names[value];
            }
        }

        //Bold KSP style GUI label
        private static GUIStyle _boldLabel = null;
        public static GUIStyle boldLabel
        {
            get
            {
                if (_boldLabel == null)
                {
                    GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                    style.fontStyle = FontStyle.Bold;
                    _boldLabel = style;
                }
                return _boldLabel;
            }
        }

        //Quick access to the part GUI events
        private BaseEvent _deploy = null, _disarm = null, _cut = null, _repack = null;
        private BaseEvent deploy
        {
            get
            {
                if (this._deploy == null) { this._deploy = Events["GUIDeploy"]; }
                return this._deploy;
            }
        }
        private BaseEvent disarm
        {
            get
            {
                if (this._disarm == null) { this._disarm = Events["GUIDisarm"]; }
                return this._disarm;
            }
        }
        private BaseEvent cutE
        {
            get
            {
                if (this._cut == null) { this._cut = Events["GUICut"]; }
                return this._cut;
            }
        }
        private BaseEvent repack
        {
            get
            {
                if (this._repack == null) { this._repack = Events["GUIRepack"]; }
                return this._repack;
            }
        }
        #endregion

        #region Fields
        //Flight
        private Vector3 dragVector = new Vector3(), pos = new Vector3d();
        private PhysicsWatch deploymentTimer = new PhysicsWatch(), failedTimer = new PhysicsWatch(), launchTimer = new PhysicsWatch(), dragTimer = new PhysicsWatch();
        private bool displayed = false, showDisarm = false;
        private double ASL, trueAlt;
        private double atmPressure, atmDensity;
        private float sqrSpeed;

        //Part
        private Animation anim = null;
        private Transform parachute = null, cap = null;
        private PhysicsWatch randomTimer = new PhysicsWatch();
        private float randomX, randomY, randomTime;
        private DeploymentStates state = DeploymentStates.NONE;

        //GUI
        private bool visible = false, hid = false;
        private int ID = Guid.NewGuid().GetHashCode();
        private GUISkin skins = HighLogic.Skin;
        private Rect window = new Rect(), drag = new Rect();
        private Vector2 scroll = new Vector2();
        #endregion

        #region Part GUI
        //Deploys the parachutes if possible
        [KSPEvent(guiActive = true, active = true, externalToEVAOnly = true, guiActiveUnfocused = true, guiName = "Deploy Chute", unfocusedRange = 5)]
        public void GUIDeploy()
        {
            ActivateRC();
        }

        //Cuts main chute chute
        [KSPEvent(guiActive = true, active = true, externalToEVAOnly = true, guiActiveUnfocused = true, guiName = "Cut chute", unfocusedRange = 5)]
        public void GUICut()
        {
            Cut();
        }

        [KSPEvent(guiActive = true, active = true, externalToEVAOnly = true, guiActiveUnfocused = true, guiName = "Disarm chute", unfocusedRange = 5)]
        public void GUIDisarm()
        {
            this.armed = false;
            this.showDisarm = false;
            this.part.stackIcon.SetIconColor(XKCDColors.White);
            this.deploy.active = true;
            DeactivateRC();
        }

        //Repacks chute from EVA if in space or on the ground
        [KSPEvent(guiActive = false, active = true, externalToEVAOnly = true, guiActiveUnfocused = true, guiName = "Repack chute", unfocusedRange = 5)]
        public void GUIRepack()
        {
            if (this.canRepack)
            {
                if (!this.canRepackCareer)
                {
                    ScreenMessages.PostScreenMessage("Only a level 1 and higher engineer can repack a parachute", 5, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }

                this.part.Effect("rcrepack");
                this.repack.guiActiveUnfocused = false;
                this.part.stackIcon.SetIconColor(XKCDColors.White);
                if (this.chuteCount != -1) { this.chuteCount--; }
                Repack();
            }
        }

        //Shows the info window
        [KSPEvent(guiActive = true, active = true, guiActiveEditor = true, guiName = "Toggle info")]
        public void GUIToggleWindow()
        {
            if (!this.visible)
            {
                List<RealChuteFAR> parachutes = new List<RealChuteFAR>();
                if (HighLogic.LoadedSceneIsEditor) { parachutes.AddRange(EditorLogic.SortedShipList.Where(p => p.Modules.Contains("RealChuteFAR")).Select(p => (RealChuteFAR)p.Modules["RealChuteFAR"])); }
                else if (HighLogic.LoadedSceneIsFlight) { parachutes.AddRange(this.vessel.FindPartModulesImplementing<RealChuteFAR>()); }
                if (parachutes.Count > 1 && parachutes.Exists(p => p.visible))
                {
                    RealChuteFAR module = parachutes.Find(p => p.visible);
                    this.window.x = module.window.x;
                    this.window.y = module.window.y;
                    module.visible = false;
                }
            }
            this.visible = !this.visible;
        }
        #endregion

        #region Action groups
        //Deploys the parachutes if possible
        [KSPAction("Deploy chute")]
        public void ActionDeploy(KSPActionParam param)
        {
            ActivateRC();
        }

        //Cuts main chute
        [KSPAction("Cut chute")]
        public void ActionCut(KSPActionParam param)
        {
            if (this.isDeployed) { Cut(); }
        }

        [KSPAction("Disarm chute")]
        public void ActionDisarm(KSPActionParam param)
        {
            if (this.armed) { GUIDisarm(); }
        }
        #endregion

        #region Methods
        //Returns the canopy area of the given Diameter
        public float GetArea(float diameter)
        {
            return (float)((diameter * diameter * Math.PI) / 4d);
        }

        //Activates the parachute
        public void ActivateRC()
        {
            this.staged = true;
            this.armed = true;
            print("[RealChute]: " + this.part.partInfo.name + " was activated in stage " + this.part.inverseStage);
        }

        //Deactiates the parachute
        public void DeactivateRC()
        {
            this.staged = false;
            print("[RealChute]: " + this.part.partInfo.name + " was deactivated");
        }

        //Copies stats from the info window to the symmetry counterparts
        private void CopyToCouterparts()
        {
            foreach (Part part in this.part.symmetryCounterparts)
            {
                RealChuteFAR module = part.Modules["RealChuteFAR"] as RealChuteFAR;
                module.minAirPressureToOpen = this.minAirPressureToOpen;
                module.deployAltitude = this.deployAltitude;
            }
        }

        //Deactivates the part
        public void StagingReset()
        {
            DeactivateRC();
            this.armed = false;
            if (this.part.inverseStage != 0) { this.part.inverseStage = this.part.inverseStage - 1; }
            else { this.part.inverseStage = Staging.CurrentStage; }
        }

        //Allows the chute to be repacked if available
        public void SetRepack()
        {
            this.part.stackIcon.SetIconColor(XKCDColors.Red);
            StagingReset();
        }

        //Drag formula calculations
        public float DragCalculation(float area)
        {
            return (float)this.atmDensity * this.sqrSpeed * area / 2000f;
        }

        //Gives the cost for this parachute
        public float GetModuleCost(float defaultCost)
        {
            return (float)Math.Round(this.deployedArea * areaCost);
        }

        //Not needed
        public Callback<Rect> GetDrawModulePanelCallback()
        {
            return null;
        }

        //Sets module info title
        public string GetModuleTitle()
        {
            return "RealChute";
        }

        //Sets part info field
        public string GetPrimaryField()
        {
            return string.Empty;
        }

        //Event when the UI is hidden (F2)
        private void HideUI()
        {
            this.hid = true;
        }

        //Event when the UI is shown (F2)
        private void ShowUI()
        {
            this.hid = false;
        }

        //Adds a random noise to the parachute movement
        private void ParachuteNoise()
        {
            float time = Time.time;
            this.parachute.Rotate(new Vector3(5 * (Mathf.PerlinNoise(time, this.randomX + Mathf.Sin(time)) - 0.5f), 5 * (Mathf.PerlinNoise(time, this.randomY + Mathf.Sin(time)) - 0.5f), 0));
        }

        //Makes the canopy follow drag direction
        private void FollowDragDirection()
        {
            if (this.dragVector.sqrMagnitude > 0)
            {
                this.parachute.rotation = Quaternion.LookRotation(this.invertCanopy ? this.dragVector : -this.dragVector, this.parachute.up);
            }
            ParachuteNoise();
        }

        //Parachute predeployment
        public void PreDeploy()
        {
            this.part.stackIcon.SetIconColor(XKCDColors.BrightYellow);
            this.part.Effect("rcpredeploy");
            this.deploymentState = DeploymentStates.PREDEPLOYED;
            this.parachute.gameObject.SetActive(true);
            this.cap.gameObject.SetActive(false);
            this.part.PlayAnimation(this.semiDeployedAnimation, this.semiDeploymentSpeed);
            this.dragTimer.Start();
        }

        //Parachute deployment
        public void Deploy()
        {
            this.part.stackIcon.SetIconColor(XKCDColors.RadioactiveGreen);
            this.part.Effect("rcdeploy");
            this.deploymentState = DeploymentStates.DEPLOYED;
            this.dragTimer.Restart();
            this.part.PlayAnimation(this.fullyDeployedAnimation, this.deploymentSpeed);
        }

        //Parachute cutting
        public void Cut()
        {
            this.part.Effect("rccut");
            this.deploymentState = DeploymentStates.CUT;
            this.parachute.gameObject.SetActive(false);
            SetRepack();
            this.dragTimer.Reset();
        }

        //Repack actions
        public void Repack()
        {
            this.deploymentState = DeploymentStates.STOWED;
            this.randomTimer.Reset();
            this.dragTimer.Reset();
            this.time = 0;
            this.cap.gameObject.SetActive(true);
        }

        //Calculates parachute deployed area
        private float DragDeployment(float time, float debutDiameter, float endDiameter)
        {
            if (!this.dragTimer.isRunning) { this.dragTimer.Start(); }

            double t = this.dragTimer.elapsed.TotalSeconds;
            this.time = (float)t;
            if (t <= time)
            {
                /* While this looks linear, area scales with the square of the diameter, and therefore
                 * Deployment will be quadratic. The previous exponential function was too slow at first and rough at the end */
                float currentDiam = Mathf.Lerp(debutDiameter, endDiameter, (float)(t / time));
                return GetArea(currentDiam);
            }
            return GetArea(endDiameter);
        }

        //Drag force vector
        private Vector3 DragForce(float debutDiameter, float endDiameter, float time)
        {
            return DragCalculation(DragDeployment(time, debutDiameter, endDiameter)) * this.dragVector;
        }
        #endregion

        #region Functions
        private void Update()
        {
            if (!CompatibilityChecker.IsAllCompatible() || !HighLogic.LoadedSceneIsFlight) { return; }

            //Makes the chute icon blink if failed
            if (this.failedTimer.isRunning)
            {
                double time = this.failedTimer.elapsed.TotalSeconds;
                if (time <= 2.5)
                {
                    if (!this.displayed)
                    {
                        ScreenMessages.PostScreenMessage("Parachute deployment failed.", 2.5f, ScreenMessageStyle.UPPER_CENTER);
                        if (this.part.ShieldedFromAirstream) { ScreenMessages.PostScreenMessage("Reason: parachute is shielded from airstream.", 2.5f, ScreenMessageStyle.UPPER_CENTER);}
                        else if (this.groundStop) { ScreenMessages.PostScreenMessage("Reason: stopped on the ground.", 2.5f, ScreenMessageStyle.UPPER_CENTER); }
                        else if (this.atmPressure == 0) { ScreenMessages.PostScreenMessage("Reason: in space.", 2.5f, ScreenMessageStyle.UPPER_CENTER); }
                        else { ScreenMessages.PostScreenMessage("Reason: too high.", 2.5f, ScreenMessageStyle.UPPER_CENTER); }
                        this.displayed = true;
                    }
                    if (time < 0.5 || (time >= 1 && time < 1.5) || time >= 2) { this.part.stackIcon.SetIconColor(XKCDColors.Red); }
                    else { this.part.stackIcon.SetIconColor(XKCDColors.White); }
                }
                else
                {
                    this.displayed = false;
                    this.part.stackIcon.SetIconColor(XKCDColors.White);
                    this.failedTimer.Reset();
                }
            }

            this.disarm.active = (this.armed || this.showDisarm);
            this.deploy.active = !this.staged && this.deploymentState != DeploymentStates.CUT;
            this.repack.guiActiveUnfocused = this.canRepack;
        }

        private void FixedUpdate()
        {
            //Flight values
            if (!CompatibilityChecker.IsAllCompatible() || !HighLogic.LoadedSceneIsFlight || FlightGlobals.ActiveVessel == null || this.part.Rigidbody == null) { return; }
            this.pos = this.part.transform.position;
            this.ASL = FlightGlobals.getAltitudeAtPos(this.pos);
            this.trueAlt = this.ASL;
            if (vessel.mainBody.pqsController != null)
            {
                double terrainAlt = vessel.pqsAltitude;
                if (!vessel.mainBody.ocean || terrainAlt > 0) { this.trueAlt -= terrainAlt; }
            }
            this.atmPressure = FlightGlobals.getStaticPressure(this.ASL, this.vessel.mainBody);
            this.atmDensity = FARAeroUtil.GetCurrentDensity(this.vessel.mainBody, this.ASL, false);
            Vector3 velocity = this.part.Rigidbody.velocity + Krakensbane.GetFrameVelocityV3f();
            this.sqrSpeed = velocity.sqrMagnitude;
            this.dragVector = -velocity.normalized;
            if (!this.staged && GameSettings.LAUNCH_STAGES.GetKeyDown() && this.vessel.isActiveVessel && (this.part.inverseStage == Staging.CurrentStage - 1 || Staging.CurrentStage == 0)) { ActivateRC(); }

            if (this.staged)
            {
                //Checks if the parachute must disarm
                if (this.armed)
                {
                    this.part.stackIcon.SetIconColor(XKCDColors.LightCyan);
                    if (this.canDeploy) { this.armed = false; }
                }
                //Parachute deployments
                else
                {
                    //Parachutes
                    if (this.canDeploy)
                    {
                        if (this.isDeployed) { FollowDragDirection(); }

                        switch (this.deploymentState)
                        {
                            case DeploymentStates.STOWED:
                                {
                                    this.part.stackIcon.SetIconColor(XKCDColors.LightCyan);
                                    if (this.deploymentClause && this.randomDeployment) { PreDeploy(); }
                                    break;
                                }

                            case DeploymentStates.PREDEPLOYED:
                                {
                                    this.part.Rigidbody.AddForceAtPosition(DragForce(0, this.preDeployedDiameter, 1f / this.semiDeploymentSpeed), this.forcePosition, ForceMode.Force);
                                    if (this.trueAlt <= this.deployAltitude && this.dragTimer.elapsed.TotalSeconds >= 1f / this.semiDeploymentSpeed) { Deploy(); }
                                    break;
                                }

                            case DeploymentStates.DEPLOYED:
                                {
                                    this.part.rigidbody.AddForceAtPosition(DragForce(this.preDeployedDiameter, this.deployedDiameter, 1f / this.deploymentSpeed), this.forcePosition, ForceMode.Force);
                                    break;
                                }

                            default:
                                break;
                        }
                    }
                    //Deactivation
                    else
                    {
                        if (this.isDeployed) { Cut(); }
                        else
                        {
                            this.failedTimer.Start();
                            StagingReset();
                        }
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (!CompatibilityChecker.IsAllCompatible() || (!HighLogic.LoadedSceneIsFlight && !HighLogic.LoadedSceneIsEditor)) { return; }
            //Hide/show UI event removal
            GameEvents.onHideUI.Remove(HideUI);
            GameEvents.onShowUI.Remove(ShowUI);
        }
        #endregion

        #region Overrides
        public override void OnStart(PartModule.StartState state)
        {
            if (!HighLogic.LoadedSceneIsEditor && !HighLogic.LoadedSceneIsFlight) { return; }
            if (!CompatibilityChecker.IsAllCompatible())
            {
                Actions.ForEach(a => a.active = false);
                Events.ForEach(e =>
                    {
                        e.active = false;
                        e.guiActive = false;
                        e.guiActiveEditor = false;
                    });
                Fields["chuteCount"].guiActive = false;
                return;
            }

            //Staging icon
            this.part.stagingIcon = "PARACHUTES";

            //I know this seems random, but trust me, it's needed, else some parachutes don't animate, because fuck you, that's why.
            this.anim = this.part.FindModelAnimators(this.capName).FirstOrDefault();

            this.cap = this.part.FindModelTransform(this.capName);
            this.parachute = this.part.FindModelTransform(this.canopyName);
            this.parachute.gameObject.SetActive(true);
            this.part.InitiateAnimation(this.semiDeployedAnimation);
            this.part.InitiateAnimation(this.fullyDeployedAnimation);
            this.parachute.gameObject.SetActive(false);

            //First initiation of the part
            if (!this.initiated)
            {
                this.initiated = true;
                this.armed = false;
                this.chuteCount = maxSpares;
                this.cap.gameObject.SetActive(true);
            }
            this.part.mass = this.totalMass;

            //Flight loading
            if (HighLogic.LoadedSceneIsFlight)
            {
                Random random = new Random();
                this.randomTime = (float)random.NextDouble();
                this.randomX = (float)(random.NextDouble() * 100);
                this.randomY = (float)(random.NextDouble() * 100);

                //Hide/show UI event addition
                GameEvents.onHideUI.Add(HideUI);
                GameEvents.onShowUI.Add(ShowUI);

                if (this.canRepack) { SetRepack(); }

                if (this.time != 0) { this.dragTimer = new PhysicsWatch(this.time); }
                if (this.deploymentState != DeploymentStates.STOWED)
                {
                    this.part.stackIcon.SetIconColor(XKCDColors.Red);
                    this.cap.gameObject.SetActive(false);
                }

                if (this.staged && this.isDeployed)
                {
                    this.parachute.gameObject.SetActive(true);
                    switch(this.deploymentState)
                    {
                        case DeploymentStates.PREDEPLOYED:
                            this.part.SkipToAnimationTime(this.semiDeployedAnimation, this.semiDeploymentSpeed, Mathf.Clamp01(this.time)); break;
                        case DeploymentStates.DEPLOYED:
                            this.part.SkipToAnimationTime(this.fullyDeployedAnimation, this.deploymentSpeed, Mathf.Clamp01(this.time)); break;

                        default:
                            break;
                    }
                }
            }

            //GUI
            this.window = new Rect(200, 100, 350, 400);
            this.drag = new Rect(0, 0, 350, 30);
        }

        public override void OnLoad(ConfigNode node)
        {
            if (!CompatibilityChecker.IsAllCompatible()) { return; }
            this.part.mass = this.totalMass;
            if (HighLogic.LoadedScene == GameScenes.LOADING)
            {
                PartModule parachute = this.part.Modules["ModuleParachute"];
                if (parachute != null) { this.part.RemoveModule(parachute); }
                if (this.deployAltitude <= 500) { this.deployAltitude += 200; }
            }
        }

        public override string GetInfo()
        {
            if (!CompatibilityChecker.IsAllCompatible()) { return string.Empty; }
            //Info in the editor part window
            this.part.mass = this.totalMass;

            StringBuilder b = new StringBuilder();
            b.AppendFormat("<b>Case mass</b>: {0}\n", this.caseMass);
            b.AppendFormat("<b>Spare chutes</b>: {0}\n", maxSpares);
            b.AppendFormat("<b>Autocut speed</b>: {0}m/s\n", this.autoCutSpeed);
            b.AppendLine("<b>Parachute material</b>: Nylon");
            b.AppendLine("<b>Drag coefficient</b>: 1.0");
            b.AppendFormat("<b>Predeployed diameter</b>: {0}m\n", this.preDeployedDiameter);
            b.AppendFormat("<b>Deployed diameter</b>: {0}m\n", this.deployedDiameter);
            b.AppendFormat("<b>Minimum deployment pressure</b>: {0}atm\n", this.minAirPressureToOpen);
            b.AppendFormat("<b>Deployment altitude</b>: {0}m\n", this.deployAltitude);
            b.AppendFormat("<b>Predeployment speed</b>: {0}s\n", 1f / this.semiDeploymentSpeed);
            b.AppendFormat("<b>Deployment speed</b>: {0}s\n", 1f / this.deploymentSpeed);
            return b.ToString();
        }
        #endregion

        #region GUI
        private void OnGUI()
        {
            if (!CompatibilityChecker.IsAllCompatible() && (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedSceneIsEditor)) { return; }

            //Info window visibility
            if (this.visible && !this.hid)
            {
                this.window = GUILayout.Window(this.ID, this.window, Window, "RealChute Info Window", this.skins.window);
            }
        }

        //Info window
        private void Window(int id)
        {
            //Header
            GUI.DragWindow(this.drag);
            GUILayout.BeginVertical();

            //Top info labels
            StringBuilder b = new StringBuilder("Part name: ").AppendLine(this.part.partInfo.title);
            b.Append("Symmetry counterparts: ").AppendLine(this.part.symmetryCounterparts.Count.ToString());
            b.Append("Part mass: ").Append(this.part.TotalMass().ToString("0.###")).Append("t");
            GUILayout.Label(b.ToString(), this.skins.label);

            //Beggining scroll
            this.scroll = GUILayout.BeginScrollView(this.scroll, false, false, this.skins.horizontalScrollbar, this.skins.verticalScrollbar, this.skins.box);
            GUILayout.Space(5);
            GUILayout.Label("General:", boldLabel, GUILayout.Width(120));

            //General labels
            b = new StringBuilder("Autocut speed: ").Append(this.autoCutSpeed).AppendLine("m/s");
            b.Append("Spare chutes: ").Append(chuteCount);
            GUILayout.Label(b.ToString(), this.skins.label);

            //Specific labels
            GUILayout.Label("___________________________________________", boldLabel);
            GUILayout.Space(3);
            GUILayout.Label("Main chute:", boldLabel, GUILayout.Width(120));
            //Initial label
            b = new StringBuilder();
            b.AppendLine("Material: Nylon");
            b.AppendLine("Drag coefficient: 1.0");
            b.Append("Predeployed diameter: ").Append(this.preDeployedDiameter).Append("m\nArea: ").Append(this.preDeployedArea.ToString("0.###")).AppendLine("m²");
            b.Append("Deployed diameter: ").Append(this.deployedDiameter).Append("m\nArea: ").Append(this.deployedArea.ToString("0.###")).Append("m²");
            GUILayout.Label(b.ToString(), this.skins.label);

            //Predeployment pressure selection
            GUILayout.Label("Predeployment pressure: " + this.minAirPressureToOpen + "atm", this.skins.label);
            if (HighLogic.LoadedSceneIsFlight)
            {
                //Predeployment pressure slider
                this.minAirPressureToOpen = GUILayout.HorizontalSlider(this.minAirPressureToOpen, 0.005f, 1, this.skins.horizontalSlider, this.skins.horizontalSliderThumb);
            }

            //Deployment altitude selection
            GUILayout.Label("Deployment altitude: " + this.deployAltitude + "m", this.skins.label);
            if (HighLogic.LoadedSceneIsFlight)
            {
                //Deployment altitude slider
                this.deployAltitude = GUILayout.HorizontalSlider(this.deployAltitude, 50, 10000, this.skins.horizontalSlider, this.skins.horizontalSliderThumb);
            }

            //Other labels
            b = new StringBuilder();
            b.Append("Predeployment speed: ").Append(1f / this.semiDeploymentSpeed).AppendLine("s");
            b.Append("Deployment speed: ").Append(1f / this.deploymentSpeed).Append("s");
            GUILayout.Label(b.ToString(), this.skins.label);

            //End scroll
            GUILayout.EndScrollView();

            //Copy button if in flight
            if (HighLogic.LoadedSceneIsFlight && this.part.symmetryCounterparts.Count > 0)
            {
                CenteredButton("Copy to others chutes", CopyToCouterparts);
            }

            //Close button
            CenteredButton("Close", () => this.visible = false);

            //Closer
            GUILayout.EndVertical();
        }

        //Creates a centered GUI button
        public static void CenteredButton(string text, Action action)
        {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(text, HighLogic.Skin.button, GUILayout.Width(150)))
            {
                action();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        #endregion
    }
}