﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPWheel
{
    /// <summary>
    /// Replacement for stock wheel motor module.<para/>
    /// Manages wheel motor input and resource use.
    /// TODO:
    /// Traction control / anti-slip.
    /// Torque curve vs rpm.
    /// </summary>
    public class KSPWheelMotor : KSPWheelSubmodule
    {
        
        [KSPField]
        public float maxMotorTorque = 0f;

        [KSPField(guiName = "Motor Torque", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 0.5f)]
        public float motorOutput = 100f;

        [KSPField]
        public float resourceAmount = 0f;

        [KSPField]
        public float maxRPM = 600f;

        [KSPField]
        public bool tankSteering = false;

        [KSPField(guiName = "Tank Steer Invert", guiActive = false, guiActiveEditor = false, isPersistant = true),
         UI_Toggle(enabledText = "Inverted", disabledText = "Normal", suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.None)]
        public bool invertSteering = false;

        [KSPField(guiName = "Tank Steer Lock", guiActive = false, guiActiveEditor = false, isPersistant = true),
         UI_Toggle(enabledText = "Locked", disabledText = "Free", suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.None)]
        public bool steeringLocked = false;

        /// <summary>
        /// If true, motor response will be inverted for this wheel.  Toggleable in editor and flight.  Persistent.
        /// </summary>
        [KSPField(guiName = "Invert Motor", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Inverted", disabledText = "Normal", suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.None)]
        public bool invertMotor;

        /// <summary>
        /// If true, motor response will be inverted for this wheel.  Toggleable in editor and flight.  Persistent.
        /// </summary>
        [KSPField(guiName = "Motor Lock", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Locked", disabledText = "Free", suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.Editor)]
        public bool motorLocked;

        [KSPField(guiActive = true, guiName = "Motor EC Use", guiUnits = "ec/s")]
        public float guiResourceUse = 0f;

        [KSPField]
        public bool useTorqueCurve = true;

        [KSPField]
        public FloatCurve torqueCurve = new FloatCurve();

        [KSPField(guiName = "Traction Control", guiActive = true, guiActiveEditor = true, isPersistant = true),
         UI_Toggle(enabledText = "Enabled", disabledText = "Disabled", suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.None)]
        public bool useTractionControl = false;

        [KSPField(guiName = "Traction Val", guiActive = true, guiActiveEditor = true),
         UI_FloatRange(minValue = 0, maxValue = 0.2f, stepIncrement = 0.001f)]
        public float tractionControl = 0.1f;

        private float fwdInput;
        public float torqueOutput;

        public void onMotorInvert(BaseField field, System.Object obj)
        {
            if (HighLogic.LoadedSceneIsEditor && part.symmetryCounterparts.Count==1)
            {
                part.symmetryCounterparts[0].GetComponent<KSPWheelMotor>().invertMotor = !invertMotor;
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            Fields[nameof(invertMotor)].uiControlEditor.onFieldChanged = onMotorInvert;
            Fields[nameof(invertSteering)].guiActive = tankSteering;
            Fields[nameof(invertSteering)].guiActiveEditor = tankSteering;
            Fields[nameof(steeringLocked)].guiActive = tankSteering;
            Fields[nameof(steeringLocked)].guiActiveEditor = tankSteering;
            if (torqueCurve.Curve.length == 0)
            {
                torqueCurve.Add(0, 1, 0, 0);
                torqueCurve.Add(1, 0, 0, 0);
            }
            if (HighLogic.LoadedSceneIsEditor && part.isClone)
            {
                invertMotor = !part.symmetryCounterparts[0].GetComponent<KSPWheelMotor>().invertMotor;
            }
            //TODO how to determine if is 'original' part or a symmetry part?
        }

        internal override void preWheelPhysicsUpdate()
        {
            base.preWheelPhysicsUpdate();
            float fI = part.vessel.ctrlState.wheelThrottle + part.vessel.ctrlState.wheelThrottleTrim;
            if (motorLocked) { fI = 0; }
            if (invertMotor) { fI = -fI; }

            if (useTractionControl)
            {
                if (wheel.longitudinalSlip > tractionControl)
                {
                    fI = 0;
                }
            }

            float rpm = wheel.rpm;
            if (fI > 0 && wheel.rpm > maxRPM) { fI = 0; }
            else if (fI < 0 && wheel.rpm < -maxRPM) { fI = 0; }

            fI *= (motorOutput * 0.01f);

            float mult = useTorqueCurve && maxRPM > 0 ? torqueCurve.Evaluate(Mathf.Abs(rpm) / maxRPM) : 1f;
            fI  = mult;

            if (tankSteering && !steeringLocked)
            {
                float rI = -(part.vessel.ctrlState.wheelSteer + part.vessel.ctrlState.wheelSteerTrim);
                if (invertSteering) { rI = -rI; }
                fI = fI + rI;
                if (fI > 1) { fI = 1; }
                if (fI < -1) { fI = -1; }
            }

            fI *= updateResourceDrain(Mathf.Abs(fI));
            
            fwdInput = fI;
            torqueOutput = wheel.motorTorque = maxMotorTorque * fwdInput * mult * controller.tweakScaleCorrector;
        }

        //TODO fix resource drain, it was causing the world to explode...
        private float updateResourceDrain(float input)
        {
            float percent = 1f;
            //if (input > 0 && resourceAmount > 0)
            //{
            //    float drain = maxMotorTorque * input * resourceAmount * TimeWarp.fixedDeltaTime;
            //    double d = part.RequestResource("ElectricCharge", drain);
            //    percent = (float)d / drain;
            //    guiResourceUse = (float)d / TimeWarp.fixedDeltaTime;
            //}
            return percent;
        }
    }
}
