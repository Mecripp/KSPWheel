﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KSPWheel
{
    public class KSPWheelDeployment : KSPWheelSubmodule
    {

        [KSPField]
        public string animationName = String.Empty;

        [KSPField]
        public float animationSpeed = 1;

        [KSPField]
        public int animationLayer = 1;

        [KSPField]
        public string tempColliderName = "deployTgt";

        [KSPField]
        public float tempColliderOffset = 0f;

        private CapsuleCollider collider;
        private Transform tempColliderTransform;
        private WheelAnimationHandler animationControl;
        private ModuleLight lightModule;
        
        [KSPAction("Toggle Gear", KSPActionGroup.Gear)]
        public void toggleGearAction(KSPActionParam param)
        {
            if (param.type == KSPActionType.Activate) { deploy(); }
            else if (param.type == KSPActionType.Deactivate) { retract(); }
        }

        [KSPEvent(guiName = "Toggle Gear", guiActive = true, guiActiveEditor = true)]
        public void toggleGearEvent()
        {
            toggleDeploy();
        }

        [KSPEvent(guiName = "Deploy Gear", guiActive = true, guiActiveEditor = true)]
        public void deploy()
        {
            if (controller == null) { return; }
            if (controller.wheelState == KSPWheelState.RETRACTED || controller.wheelState == KSPWheelState.RETRACTING) { toggleDeploy(); }
        }

        [KSPEvent(guiName = "Retract Gear", guiActive = true, guiActiveEditor = true)]
        public void retract()
        {
            if (controller == null) { return; }
            if (controller.wheelState == KSPWheelState.DEPLOYED || controller.wheelState == KSPWheelState.DEPLOYING) { toggleDeploy(); }
        }

        private void toggleDeploy()
        {
            if (animationControl == null)
            {
                MonoBehaviour.print("Animation control is null!");
                return;
            }
            if (controller.wheelState == KSPWheelState.DEPLOYED || controller.wheelState == KSPWheelState.DEPLOYING)
            {
                controller.wheelState = KSPWheelState.RETRACTING;
                animationControl.setToAnimationState(controller.wheelState, false);
                if (collider != null)
                {
                    collider.height = wheel.length + wheel.radius*2 - wheel.compressionDistance;
                    collider.center = new Vector3(0, -collider.height * 0.5f + collider.radius, 0);
                    collider.enabled = true;
                }
            }
            else if (controller.wheelState == KSPWheelState.RETRACTED || controller.wheelState == KSPWheelState.RETRACTING)
            {
                controller.wheelState = KSPWheelState.DEPLOYING;
                animationControl.setToAnimationState(controller.wheelState, false);
                if (collider != null)
                {
                    collider.height = wheel.length + wheel.radius * 2;// - wheel.compressionDistance;
                    collider.center = new Vector3(0, -collider.height * 0.5f + collider.radius, 0);
                    collider.enabled = true;
                }
            }
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
        }

        internal override void postControllerSetup()
        {
            base.postControllerSetup();
            animationControl = new WheelAnimationHandler(this, animationName, animationSpeed, animationLayer, controller.wheelState);
            Events[nameof(deploy)].active = controller.wheelState == KSPWheelState.RETRACTED;
            Events[nameof(retract)].active = controller.wheelState == KSPWheelState.DEPLOYED;
            animationControl.setToAnimationState(controller.wheelState, false);

            lightModule = part.GetComponent<ModuleLight>();
            if (lightModule != null && controller.wheelState == KSPWheelState.DEPLOYED)
            {
                lightModule.LightsOn();
            }
            if (vessel != null)
            {
                vessel.ActionGroups.SetGroup(KSPActionGroup.Gear, controller.wheelState == KSPWheelState.DEPLOYED);
            }
            tempColliderTransform = part.transform.FindRecursive(tempColliderName);
        }

        internal override void postWheelCreated()
        {
            base.postWheelCreated();
            if (tempColliderTransform != null)
            {
                GameObject standInCollider = new GameObject("KSPWheelTempCollider");
                //nest it to the wheel collider, with y+ orientation
                standInCollider.transform.NestToParent(tempColliderTransform);
                Vector3 pos = standInCollider.transform.localPosition;
                pos.y += tempColliderOffset;
                standInCollider.transform.localPosition = pos;
                standInCollider.layer = 26;
                collider = standInCollider.AddComponent<CapsuleCollider>();
                collider.radius = wheel.radius;
                collider.height = wheel.length + wheel.radius * 2;
                collider.center = new Vector3(0, -collider.height * 0.5f + collider.radius, 0);
                collider.enabled = controller.wheelState == KSPWheelState.RETRACTING || controller.wheelState == KSPWheelState.DEPLOYING;
            }
        }

        internal override void preWheelFrameUpdate()
        {
            base.preWheelFrameUpdate();
            animationControl.updateAnimationState();
        }

        internal override void preWheelPhysicsUpdate()
        {
            base.preWheelPhysicsUpdate();
            if (collider != null)
            {
                collider.enabled = controller.wheelState == KSPWheelState.RETRACTING || controller.wheelState==KSPWheelState.DEPLOYING;
            }
        }

        /// <summary>
        /// Callback from animationControl for when an animation transitions from one state to another
        /// </summary>
        /// <param name="state"></param>
        public void onAnimationStateChanged(KSPWheelState state)
        {
            controller.wheelState = state;
            MonoBehaviour.print("Set wheel state to: " + state);
            if (state == KSPWheelState.RETRACTED)
            {
                //TODO reset suspension and steering transforms to neutral?
                if (lightModule != null) { lightModule.LightsOff(); }
            }
            else if (state == KSPWheelState.DEPLOYED)
            {
                if (lightModule != null) { lightModule.LightsOn(); }
            }
        }

        private void updateCollider()
        {

        }
    }
}
