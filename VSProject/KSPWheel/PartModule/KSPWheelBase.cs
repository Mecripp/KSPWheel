﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPWheel
{
    /// <summary>
    /// A replacement for the stock wheel system that uses the KSPWheelCollider class for phsyics handling.
    /// Intended to be a fully-functional (but possibly not fully-equivalent) replacement for the stock wheel modules and U5 WheelCollider component
    /// </summary>
    public class KSPWheelBase : PartModule
    {

        #region REGION - Basic config parameters

        /// <summary>
        /// Name of the transform that the wheel collider component should be attached to/manipulate.
        /// </summary>
        [KSPField]
        public string wheelColliderName;

        /// <summary>
        /// Name of the transform that should be rotated around its specified axis for wheel rotation<para/>
        /// May be null if no transform should be rotated.  Will accept CSV list if multiple wheels should be animated.
        /// </summary>
        [KSPField]
        public string wheelPivotName;

        /// <summary>
        /// The axis on which to rotate the wheel pivot transform.  Defaults to X-axis.
        /// </summary>
        [KSPField]
        public Vector3 wheelPivotAxis = Vector3.left;

        /// <summary>
        /// The raycast mask to use for the wheel-collider suspension sweep. <para/>
        /// By default ignore layers 26 and 10 (wheelCollidersIgnore & scaledScenery)
        /// </summary>
        [KSPField]
        public int raycastMask = ~(1 << 26 | 1 << 10);
                
        /// <summary>
        /// Determines how far above the initial position in the model that the wheel-collider should be located.
        /// This is needed as the setup for stock models varies widely for wheel-collider positioning;
        /// some have it near the top of suspension travel, others at the bottom.
        /// Needs to be set on a per-part/model basis.
        /// </summary>
        [KSPField]
        public float wheelColliderOffset = 0f;
        
        /// <summary>
        /// An offset to the rotation of the wheel collider, in euler angles
        /// </summary>
        [KSPField]
        public Vector3 wheelColliderRotation = Vector3.zero;

        [KSPField]
        public float wheelRadius = 0.25f;

        [KSPField]
        public float wheelMass = 0.25f;

        [KSPField]
        public float suspensionTravel = 0.25f;

        [KSPField(guiName = "RideHeight", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_FloatRange(minValue = 0.05f, maxValue = 1, stepIncrement = 0.05f, suppressEditorShipModified = true)]
        public float suspensionTarget = 0.5f;

        [KSPField(guiName = "LoadRating", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_FloatRange(minValue = 0.05f, maxValue = 5, stepIncrement = 0.05f, suppressEditorShipModified = true)]
        public float loadRating = 2.5f;

        [KSPField]
        public float minLoadRating = 0.05f;

        [KSPField]
        public float maxLoadRating = 5f;

        [KSPField(guiName = "DampRatio", guiActive = true, guiActiveEditor = true, isPersistant = true),
        UI_FloatRange(minValue = 0.05f, maxValue = 2, stepIncrement = 0.025f, suppressEditorShipModified = true)]
        public float dampRatio = 0.65f;

        [KSPField]
        public float minDampRatio = 0.05f;

        [KSPField]
        public float maxDampRatio = 2f;
        
        /// <summary>
        /// If true the steering will be locked to zero and will not respond to steering input.
        /// </summary>
        [KSPField(guiName = "Auto-Tune", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Enabled", disabledText = "Disabled", suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.None)]
        public bool autoTuneSuspension = false;

        #endregion

        #region REGION - Persistent data

        [KSPField(isPersistant = true)]
        public string persistentState = KSPWheelState.DEPLOYED.ToString();

        [KSPField(isPersistant = true)]
        public bool grounded = false;

        [KSPField]
        public bool defaultCompressed = false;

        #endregion

        #region REGION - Private working/cached variables

        public KSPWheelState wheelState = KSPWheelState.DEPLOYED;

        private List<KSPWheelSubmodule> subModules = new List<KSPWheelSubmodule>();
        private Transform wheelColliderTransform;//the transform that the wheel-collider is attached to
        private Transform[] wheelPivotTransforms;//
        private KSPWheelCollider wheel;
        private GameObject bumpStopGameObject;
        private SphereCollider bumpStopCollider;
        private GameObject boundsGameObject;
        private BoxCollider boundsCollider;

        #endregion

        #region REGION - GUI Handling methods

        public void onLoadUpdated(BaseField field, object obj)
        {
            float suspensionSpring, suspensionDamper;
            calcSuspension(loadRating, suspensionTravel, suspensionTarget, dampRatio, out suspensionSpring, out suspensionDamper);
            if (wheel != null)
            {
                wheel.spring = suspensionSpring;
                wheel.damper = suspensionDamper;
            }
        }

        #endregion

        #region REGION - Standard KSP/Unity Overrides

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            //NOOP?
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.SetValue("persistentState", persistentState, true);
        }
        
        /// <summary>
        /// Initializes wheel parameters, removes stock wheel collider component, instantiates custom wheel collider component container, sets up animation handling (if needed)
        /// </summary>
        /// <param name="state"></param>
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            //Utils.printHierarchy(part.gameObject);
            wheelState = (KSPWheelState)Enum.Parse(typeof(KSPWheelState), persistentState);
            locateTransforms();

            WheelCollider collider = wheelColliderTransform.GetComponent<WheelCollider>();
            if (collider != null)
            {
                GameObject.Destroy(collider);//remove that stock crap, replace it with some new hotness below in the Start() method
            }
            bumpStopGameObject = new GameObject("KSPWheelBumpStop-" + wheelColliderName);
            bumpStopGameObject.layer = 26;
            bumpStopGameObject.transform.NestToParent(wheelColliderTransform);
            bumpStopCollider = bumpStopGameObject.AddComponent<SphereCollider>();
            bumpStopCollider.center = Vector3.zero;
            bumpStopCollider.radius = wheelRadius;
            PhysicMaterial mat = new PhysicMaterial("TEST");
            mat.bounciness = 0.0f;
            mat.dynamicFriction = 0;
            mat.staticFriction = 0;
            bumpStopCollider.material = mat;
            bumpStopCollider.enabled = wheelState == KSPWheelState.DEPLOYED;

            BaseField field = Fields[nameof(loadRating)];
            field.uiControlEditor.onFieldChanged = field.uiControlFlight.onFieldChanged = onLoadUpdated;
            UI_FloatRange rng = (UI_FloatRange)field.uiControlFlight;
            if (rng != null)
            {
                rng.minValue = minLoadRating;
                rng.maxValue = maxLoadRating;
                rng.stepIncrement = 0.1f;
            }

            field = Fields[nameof(dampRatio)];
            field.uiControlEditor.onFieldChanged = field.uiControlFlight.onFieldChanged = onLoadUpdated;
            rng = (UI_FloatRange)field.uiControlFlight;
            if (rng != null)
            {
                rng.minValue = minDampRatio;
                rng.maxValue = maxDampRatio;
                rng.stepIncrement = 0.1f;
            }

            field = Fields[nameof(suspensionTarget)];
            field.uiControlEditor.onFieldChanged = field.uiControlFlight.onFieldChanged = onLoadUpdated;

            //TODO -- there has got to be an easier way to handle these; perhaps check if the collider is part of the 
            // model hierarchy for the part/vessel?
            if (HighLogic.LoadedSceneIsFlight)
            {
                Collider[] colliders = part.GetComponentsInChildren<Collider>();
                int len = colliders.Length;
                for (int i = 0; i < len; i++)
                {
                    // set all colliders in the part to wheel-collider-ignore layer;
                    // no stock models that I've investigated have colliders on the same object as meshes, they all use separate colliders
                    //colliders[i].gameObject.layer = 26;//wheelcollidersignore
                    // remove stock 'collisionEnhancer' collider from wheels, if present;
                    // these things screw with wheel updates/raycasting, and cause improper collisions on wheels
                    if (colliders[i].gameObject.name.ToLower() == "collisionenhancer")
                    {
                        GameObject.Destroy(colliders[i].gameObject);
                    }
                }
            }
            part.collider = null;//clear the part collider that causes explosions.... collisions still happen, but things won't break
            
            wheelColliderTransform.Rotate(wheelColliderRotation, Space.Self);
            wheelColliderTransform.localPosition += Vector3.up * wheelColliderOffset;
        }

        /// <summary>
        /// Updates the wheel collider component physics if it is not broken or retracted
        /// </summary>
        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) { return; }
            if (!FlightGlobals.ready || !FlightDriver.fetch) { return; }
            //workaround for part rigidbody not being present during start/load or initial fixedupdate ticks
            //TODO can set it up in a coroutine, continuing to yield until rigidbody is present?
            //      is that really a simpler solution? still have  to check for null each fixed-update tick until the wheel is present
            //      but might allow for specific co-routines to be ran for each specific update function -- one for wait and check, one for actual updates
            //      WHAT is the overhead cost of coroutines
            if (wheel == null)
            {
                Rigidbody rb = part.GetComponent<Rigidbody>();
                if (part.parent != null) { rb = part.parent.GetComponent<Rigidbody>(); }
                if (rb == null)
                {
                    return;
                }
                else
                {
                    wheel = wheelColliderTransform.gameObject.AddComponent<KSPWheelCollider>();
                    wheel.rigidbody = rb;
                    wheel.radius = wheelRadius;
                    wheel.mass = wheelMass;
                    wheel.length = suspensionTravel;
                    wheel.target = 0f;// suspensionTarget;
                    float suspensionSpring, suspensionDamper;
                    calcSuspension(loadRating, suspensionTravel, suspensionTarget, dampRatio, out suspensionSpring, out suspensionDamper);
                    wheel.spring = suspensionSpring;
                    wheel.damper = suspensionDamper;
                    //wheel.isGrounded = grounded;
                    wheel.raycastMask = raycastMask;
                    onWheelCreated(wheelColliderTransform, wheel);
                }
            }

            if (part.collisionEnhancer != null) { part.collisionEnhancer.OnTerrainPunchThrough = CollisionEnhancerBehaviour.DO_NOTHING; }
            bumpStopCollider.enabled = wheelState == KSPWheelState.DEPLOYED;

            if (wheelState == KSPWheelState.DEPLOYED)
            {
                wheel.radius = wheelRadius;
                wheel.length = suspensionTravel;
                wheel.gravityVector = vessel.gravityForPos;
                if (autoTuneSuspension)
                {
                    updateSuspension();
                }
                for (int i = 0; i < subModules.Count; i++) { subModules[i].preWheelPhysicsUpdate(); }
                wheel.updateWheel();
                for (int i = 0; i < subModules.Count; i++) { subModules[i].postWheelPhysicsUpdate(); }
            }

            updateLandedState();
        }

        /// <summary>
        /// Updates the mesh animation status from the wheel collider components current state (steer angle, wheel rotation, suspension compression)
        /// </summary>
        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight || !FlightGlobals.ready || !FlightDriver.fetch || wheel == null) { return; }

            int len = subModules.Count;
            for (int i = 0; i < len; i++)
            {
                subModules[i].preWheelFrameUpdate();
            }

            //TODO block/reset input state when not deployed, re-orient wheels to default (zero steering rotation) when retracted/ing?
            if (wheelPivotTransforms != null && wheelPivotTransforms.Length>0)
            {
                len = wheelPivotTransforms.Length;
                for (int i = 0; i < len; i++)
                {
                    wheelPivotTransforms[i].Rotate(wheel.perFrameRotation, 0, 0, Space.Self);
                }
            }
        }

        #endregion

        #region REGION - Custom update methods

        [KSPField]
        float susRes = 1f;

        /// <summary>
        /// Auto-suspension tuning.
        /// Works, but causes interference with traction and normal suspension response.
        /// </summary>
        private void updateSuspension()
        {
            float target = wheel.springForce * 0.1f;
            if (target < minLoadRating) { target = minLoadRating; }
            if (target > maxLoadRating) { target = maxLoadRating; }
            loadRating = Mathf.Lerp(loadRating, target, Time.deltaTime * susRes);
            float suspensionSpring, suspensionDamper;
            calcSuspension(loadRating, suspensionTravel, suspensionTarget, dampRatio, out suspensionSpring, out suspensionDamper);
            wheel.spring = suspensionSpring;
            wheel.damper = suspensionDamper;
        }

        internal void onWheelConfigChanged(KSPWheelSubmodule module)
        {
            int len = subModules.Count;
            for (int i = 0; i < len; i++)
            {
                subModules[i].onWheelConfigChanged(module);
            }
        }

        internal void addSubmodule(KSPWheelSubmodule module)
        {
            subModules.AddUnique(module);
        }

        internal void removeSubmodule(KSPWheelSubmodule module)
        {
            subModules.Remove(module);
        }

        private void onWheelCreated(Transform tr, KSPWheelCollider wheel)
        {
            int len = subModules.Count;
            for (int i = 0; i < len; i++)
            {
                subModules[i].onWheelCreated(tr, wheel);
            }
        }

        //TODO also need to check the rest of the parts' colliders for contact/grounded state somehow
        private void updateLandedState()
        {
            grounded = wheel.isGrounded;
            part.GroundContact = grounded;
            vessel.checkLanded();
        }
        
        /// <summary>
        /// Input load in tons, suspension length, target (0-1), and desired damp ratio (1 = critical)
        /// and output spring and damper for that load and ratio
        /// </summary>
        private void calcSuspension(float load, float length, float target, float dampRatio, out float spring, out float damper)
        {
            float targetCompression = target * length;
            if (targetCompression <= 0) { targetCompression = 0.01f; }
            spring = load * 10 / targetCompression;
            //damper = 2 * Mathf.Sqrt(load * spring) * dampRatio;
            float k = spring;
            float o = Mathf.Sqrt(k / load);//natural frequency
            float cd = 2 * load * o;//critical damping coefficient
            //cd = 2 * Mathf.Sqrt(k * load);
            damper = cd * dampRatio;
        }

        /// <summary>
        /// Locate the wheel-pivot transforms from the list of wheel-pivot names (may be singular or CSV list), will find multiple same-named transforms
        /// ALL of them must rotate on the same axis (x-axis by default, currently not configurable)
        /// </summary>
        private void locateTransforms()
        {
            wheelColliderTransform = part.transform.FindRecursive(wheelColliderName);
            String[] pivotNames = wheelPivotName.Split(',');
            List<Transform> transforms = new List<Transform>();
            int len = pivotNames.Length;
            for (int i = 0; i < len; i++)
            {
                part.transform.FindRecursiveMulti(pivotNames[i].Trim(), transforms);
            }
            wheelPivotTransforms = transforms.ToArray();
        }

        #endregion

    }

}
