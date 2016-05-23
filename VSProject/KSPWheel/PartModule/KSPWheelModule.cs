﻿using System;
using UnityEngine;

namespace KSPWheel
{
    /// <summary>
    /// A replacement for the stock wheel system that uses the KSPWheelCollider class for phsyics handling.
    /// Intended to be a fully-functional (but possibly not fully-equivalent) replacement for the stock wheel modules and U5 WheelCollider component
    /// </summary>
    public class KSPWheelModule : PartModule
    {
        #region REGION - Basic wheel parameters that cannot be pulled from the U5 WheelCollider component

        /// <summary>
        /// Name of the transform that the wheel collider component should be attached to/manipulate.
        /// </summary>
        [KSPField]
        public string wheelColliderName;

        /// <summary>
        /// Name of the transform that should be rotated around its X axis for wheel rotation<para/>
        /// May be null if no transform should be rotated
        /// </summary>
        [KSPField]
        public string wheelPivotName;

        /// <summary>
        /// Name of the mesh that represents the 'non-busted wheel'<para/>
        /// May be null if no wheel mesh exists or should be manipulated
        /// </summary>
        [KSPField]
        public string wheelName;

        /// <summary>
        /// Name of the mesh that represents the 'busted wheel'<para/>
        /// May be null if no busted wheel mesh exists or should be manipulated (if not present, and wheel is breakable, the normal mesh will be used if it is present)
        /// </summary>
        [KSPField]
        public string bustedWheelName;

        /// <summary>
        /// Name of the transform that should be moved for suspension<para/>
        /// May be null if there is no visual response to suspension forces for the wheel (repulsors)
        /// </summary>
        [KSPField]
        public string suspensionName;

        /// <summary>
        /// Name of the transform used for steering rotation.  This transform will be rotated around its Y axis, with the default orientation of the model representing straight forward<para/>
        /// May be null if there is no steering on the model; may re-use the suspension transform for steering transform if model is setup in such a manner.
        /// </summary>
        [KSPField]
        public string steeringName;

        /// <summary>
        /// Determines if this wheel should use tank-steering.  This will adjust the fwd/reverse input for steering input rather than manipulating the orientation of the wheel.
        /// </summary>
        [KSPField]
        public bool tankSteering;

        /// <summary>
        /// If true, steering will be inverted for this wheel.  Toggleable in editor and flight.  Persistent.
        /// </summary>
        [KSPField(guiName ="Invert Steering", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Inverted", disabledText = "Normal", suppressEditorShipModified = true)]
        public bool invertSteering;

        /// <summary>
        /// If true the steering will be locked to zero and will not respond to steering input.
        /// </summary>
        [KSPField(guiName = "Steering Lock", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Locked", disabledText = "Free", suppressEditorShipModified = true)]
        public bool steeringLocked;

        /// <summary>
        /// If true, motor response will be inverted for this wheel.  Toggleable in editor and flight.  Persistent.
        /// </summary>
        [KSPField(guiName = "Invert Motor", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Inverted", disabledText = "Normal", suppressEditorShipModified = true)]
        public bool invertMotor;

        /// <summary>
        /// If true, motor response will be inverted for this wheel.  Toggleable in editor and flight.  Persistent.
        /// </summary>
        [KSPField(guiName = "Motor Lock", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Locked", disabledText = "Enabled", suppressEditorShipModified = true)]
        public bool motorLocked;

        #endregion

        #region REGION - Optional wheel parameters that may vary on a part to part basis
        [KSPField]
        public float maxSteeringAngle;
        [KSPField]
        public float maxMotorTorque;
        [KSPField]
        public float maxBrakeTorque;
        [KSPField]
        public float minBrakeTorque;//for landing legs, brakes==always on
        [KSPField]
        public float suspensionOffset = 0f;
        #endregion

        #region REGION - Optional wheel parameters that may be loaded from the WheelCollider if present
        [KSPField]
        public float wheelRadius = -1;
        [KSPField]
        public float wheelMass = -1;
        [KSPField]
        public float suspensionTravel = -1;
        [KSPField]
        public float suspensionTarget = -1;
        [KSPField]
        public float suspensionSpring = -1;
        [KSPField]
        public float suspensionDamper = -1;
        [KSPField]
        public float motorTorque = -1;
        [KSPField]
        public float brakeTorque = -1;
        #endregion

        #region REGION - Animation handling
        [KSPField]
        public string animationName;
        [KSPField]
        public float animationSpeed;
        [KSPField]
        public int animationLayer;
        #endregion

        #region REGION - Persistent data
        [KSPField(isPersistant = true)]
        public string persistentState = KSPWheelState.DEPLOYED.ToString();

        [KSPField(isPersistant = true)]
        public bool grounded = false;
        #endregion

        #region REGION - Private working/cached variables
        private Transform wheelColliderTransform;
        private Transform wheelMesh;
        private Transform bustedWheelMesh;
        private Transform suspensionMesh;
        private Transform steeringMesh;

        private KSPWheelCollider wheel;
        private KSPWheelState wheelState = KSPWheelState.DEPLOYED;
        private WheelAnimationHandler animationControl;

        private Vector3 suspensionLocalOrigin;
        #endregion

        #region REGION - GUI Handling methods

        //TODO -- disable when no animation is present
        [KSPAction("Toggle Gear")]
        public void toggleGearAction(KSPActionParam param)
        {
            if (param.type == KSPActionType.Activate) { deploy(); }
            else if (param.type == KSPActionType.Deactivate) { retract(); }
        }

        //TODO -- disable when no animation is present
        [KSPEvent(guiName = "Toggle Gear", guiActive = true, guiActiveEditor = true)]
        public void toggleGearEvent()
        {
            toggleDeploy();
        }

        //TODO -- enable/disable for broken status
        [KSPEvent(guiName = "Repair Gear", guiActive = false, guiActiveEditor = false)]
        public void repairWheel()
        {

        }
        
        private void deploy()
        {
            if (wheelState == KSPWheelState.RETRACTED || wheelState == KSPWheelState.RETRACTING) { toggleDeploy(); }
        }
        
        private void retract()
        {
            if (wheelState == KSPWheelState.DEPLOYED || wheelState == KSPWheelState.DEPLOYING) { toggleDeploy(); }
        }

        private void toggleDeploy()
        {
            if (animationControl == null) { return; }
            if (wheelState == KSPWheelState.DEPLOYED || wheelState == KSPWheelState.DEPLOYING)
            {
                animationControl.setToAnimationState(KSPWheelState.RETRACTING, false);
            }
            else if (wheelState == KSPWheelState.RETRACTED || wheelState == KSPWheelState.RETRACTING)
            {
                animationControl.setToAnimationState(KSPWheelState.DEPLOYING, false);
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
            wheelState = (KSPWheelState)Enum.Parse(typeof(KSPWheelState), persistentState);
            wheelColliderTransform = part.transform.FindRecursive(wheelColliderName);
            wheelMesh = part.transform.FindRecursive(wheelName);
            bustedWheelMesh = part.transform.FindRecursive(bustedWheelName);
            suspensionMesh = part.transform.FindRecursive(suspensionName);
            suspensionLocalOrigin = suspensionMesh.transform.localPosition;
            steeringMesh = part.transform.FindRecursive(steeringName);
            if (!string.IsNullOrEmpty(animationName)) { animationControl = new WheelAnimationHandler(this, animationName, animationSpeed, animationLayer, wheelState); }
            WheelCollider collider = wheelColliderTransform.GetComponent<WheelCollider>();
            if (collider != null)
            {
                wheelRadius = collider.radius;
                suspensionTravel = suspensionTravel==-1? collider.suspensionDistance : suspensionTravel;
                suspensionTarget = suspensionTarget==-1? collider.suspensionSpring.targetPosition : suspensionTarget;
                suspensionSpring = suspensionSpring == -1 ? collider.suspensionSpring.spring : suspensionSpring ;
                suspensionDamper = suspensionDamper == -1 ? collider.suspensionSpring.damper : suspensionDamper;
                wheelMass = wheelMass == -1 ? collider.mass : wheelMass;
            }
            Component.Destroy(collider);//remove that stock crap, replace it with some new hotness below in the Start() method
            if (animationControl != null) { animationControl.setToAnimationState(wheelState, false); }
            Events["toggleGearEvent"].active = animationControl != null;
            Actions["toggleGearAction"].active = animationControl != null;
            Events["repairWheel"].active = wheelState == KSPWheelState.BROKEN;
            Collider[] colliders = part.GetComponentsInChildren<Collider>();
            int len = colliders.Length;
            for (int i = 0; i < len; i++)
            {
                colliders[i].enabled = false;
            }
            wheelColliderTransform.localPosition += Vector3.up * suspensionTravel;
            if (bustedWheelMesh != null) { bustedWheelMesh.gameObject.SetActive(false); }
        }

        /// <summary>
        /// Creates the replacement wheel-collider component and initializes its config parameters from those loaded from the u5-WC component
        /// </summary>
        public void Start()
        {
            //delaying until Start as the part.rigidbody is not initialized until ?? (need to find out when...)
            wheel = new KSPWheelCollider(wheelColliderTransform.gameObject, part.gameObject.GetComponent<Rigidbody>());
            wheel.wheel = wheelColliderTransform.gameObject;
            wheel.wheelRadius = wheelRadius;
            wheel.wheelMass = wheelMass;
            wheel.suspensionLength = suspensionTravel;
            wheel.target = suspensionTarget;
            wheel.spring = suspensionSpring;
            wheel.damper = suspensionDamper;
            wheel.motorTorque = motorTorque;
            wheel.brakeTorque = brakeTorque;
            wheel.grounded = grounded;
        }

        /// <summary>
        /// Updates the wheel collider component physics if it is not broken or retracted
        /// </summary>
        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) { return; }
            
            if (wheel.rigidBody == null)
            {
                wheel.rigidBody = part.GetComponent<Rigidbody>();
            }
            if (wheel.rigidBody == null)
            {
                MonoBehaviour.print("Part rigidbody is null, cannot update!");
                return;
            }
            sampleInput();
            //Update the wheel physics state as long as it is not broken or fully retracted
            //yes, this means updates happen during deploy and retract animations (as they should! -- wheels don't just work when they are deployed...).
            if (wheelState != KSPWheelState.BROKEN && wheelState != KSPWheelState.RETRACTED)
            {
                wheel.UpdateWheel();
            }
            grounded = wheel.grounded;
            part.GroundContact = grounded;
            vessel.checkLanded();
        }

        //TODO -- input handling
        /// <summary>
        /// Updates the mesh animation status from the wheel collider components current state (steer angle, wheel rotation, suspension compression)
        /// </summary>
        public void Update()
        {
            if (animationControl != null) { animationControl.updateAnimationState(); }
            //TODO reset input state on animation state changes, re-orient wheels to default when retracted/ing?
            if (!HighLogic.LoadedSceneIsFlight || wheelState==KSPWheelState.BROKEN || wheelState==KSPWheelState.RETRACTED) { return; }
            
            //TODO -- input handling/updating
            if (suspensionMesh != null)
            {
                suspensionMesh.localPosition = suspensionLocalOrigin + (Vector3.up * wheel.compressionDistance) + (Vector3.up * suspensionOffset);
            }
            if (steeringMesh != null)
            {
                float angle = wheel.currentSteerAngle;
                if (invertSteering) { angle = -angle; }
                steeringMesh.localRotation = Quaternion.Euler(0, angle, 0);
            }
            if (wheelMesh != null)
            {
                wheelMesh.transform.position = wheel.wheelMeshPosition + wheel.rigidBody.velocity * TimeWarp.fixedDeltaTime;
                wheelMesh.transform.Rotate(wheel.wheelRPM, 0, 0, Space.Self);
            }
        }

        #endregion

        #region REGION - Custom update methods

        /// <summary>
        /// Temporariy input handling code
        /// </summary>
        private void sampleInput()
        {
            float left = Input.GetKey(KeyCode.A) ? -1 : 0;
            float right = Input.GetKey(KeyCode.D) ? 1 : 0;
            float fwd = Input.GetKey(KeyCode.W) ? 1 : 0;
            float rev = Input.GetKey(KeyCode.S) ? -1 : 0;
            if (motorLocked) { fwd = rev = 0; }
            if (steeringLocked) { left = right = 0; }
            float fwdInput = fwd + rev;
            float rotInput = left + right;
            if (invertSteering) { rotInput = -rotInput; }
            if (tankSteering)
            {
                fwdInput = fwdInput + rotInput;
                if (fwdInput > 1) { fwdInput = 1; }
                if (fwdInput < -1) { fwdInput = -1; }
            }
            wheel.setInputState(fwdInput, rotInput);
        }

        //TODO -- ??
        /// <summary>
        /// Callback from animationControl for when an animation transitions from one state to another
        /// </summary>
        /// <param name="state"></param>
        public void onAnimationStateChanged(KSPWheelState state)
        {
            wheelState = state;
        }

        /// <summary>
        /// Called from the KSPWheelCollider on first ground contact<para/>
        /// The input Vector3 is the wheel-local impact velocity.  Relative impact speed can be derived from localImpactVelocity.magnitude
        /// </summary>
        /// <param name="localImpactVelocity"></param>
        public void onWheelImpact(Vector3 localImpactVelocity)
        {

        }

        #endregion

    }

}
