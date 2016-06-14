﻿using System;
using UnityEngine;

namespace KSPWheel
{

    public class KSPWheelCollider
    {

        #region REGION - Public Accessible values

        /// <summary>
        /// The game object this script should be attached to / affect, set from constructor
        /// </summary>
        public readonly GameObject wheel;

        // TODO really should be read-only, being grabbed from wheel game object when collider is initialized
        // but silly KSP doesn't have RB's on the PART during MODULE initialization; needs to be delayed until first fixed update at least?
        /// <summary>
        /// The rigidbody that this wheel will apply forces to and sample velocity from, set from constructor
        /// </summary>
        public Rigidbody rigidBody;

        #endregion ENDREGION - Public Accessible values

        #region REGION - Private variables

        //externally set values
        private float currentWheelMass = 1f;
        private float currentWheelRadius = 0.5f;
        private float currentSuspensionLength = 1f;
        private float currentSuspensionTarget = 0f;
        private float currentSpring = 10f;
        private float currentDamper = 2f;
        private float currentFwdFrictionCoef = 1f;
        private float currentSideFrictionCoef = 1f;
        private float currentSurfaceFrictionCoef = 1f;
        private float currentSteerAngle = 0f;
        private float currentMotorTorque = 0f;
        private float currentBrakeTorque = 0f;
        private float currentMomentOfInertia = 1.0f * 0.5f * 0.5f * 0.5f;//moment of inertia of wheel; used for mass in acceleration calculations regarding wheel angular velocity.  MOI of a solid cylinder = ((m*r*r)/2)
        private int currentRaycastMask = ~(1 << 26);//default cast to all layers except 26; 1<<26 sets 26 to the layer; ~inverts all bits in the mask (26 = KSP WheelColliderIgnore layer)
        private KSPWheelFrictionType currentFrictionModel = KSPWheelFrictionType.STANDARD;
        private KSPWheelSweepType currentSweepType = KSPWheelSweepType.RAY;

        //sticky-friction vars;
        //TODO -- add get/set methods for these to expose them for configuration
        //TODO -- finish implementing sticky friction stuff =\
        private float maxStickyVelocity = 0.00f;
        private float sideStickyTimeMax = 0.25f;
        private float fwdStickyTimeMax = 0.25f;
        private float sideStickyTimer = 0;
        private float fwdStickyTimer = 0;
        private Vector3 wF, wR;
        
        //internal friction model values
        private float prevFSpring;
        private float currentSuspensionCompression = 0f;
        private float prevSuspensionCompression = 0f;
        private float currentAngularVelocity = 0f;//angular velocity of wheel; rotations in radians per second
        private float vSpring;//linear velocity of spring in m/s, derived from prevCompression - currentCompression along suspension axis
        private float fDamp;//force exerted by the damper this physics frame, in newtons

        private bool grounded = false;
        private Vector3 wheelUp;
        private Vector3 wheelForward;
        private Vector3 wheelRight;
        private Vector3 localVelocity;
        private Vector3 localForce;
        private float vWheel;
        private float vWheelDelta;
        private float sLong;
        private float sLat;
        private Vector3 hitPoint;//world-space position of contact patch
        private Vector3 hitNormal;
        private Collider hitCollider;

        //cached internal utility vars
        private float inertiaInverse;//cached inertia inverse used to eliminate division operations from per-tick update code
        private float radiusInverse;//cached radius inverse used to eliminate division operations from per-tick update code
        private float massInverse;//cached mass inverse used to eliminate division operations from per-tick update code

        //run-time references to various objects
        private ConfigurableJoint stickyJoint;//the joint used for sticky friction
        private Action<Vector3> onImpactCallback;//simple blind callback for when the wheel changes from !grounded to grounded, the input variable is the wheel-local impact velocity

        private KSPWheelFrictionCurve fwdFrictionCurve;//current forward friction curve
        private KSPWheelFrictionCurve sideFrictionCurve;//current sideways friction curve
        
        #endregion ENDREGION - Private variables

        #region REGION - Public accessible methods, Constructor, API get/set methods

        /// <summary>
        /// Initialize a wheel-collider object for the given GameObject (the wheel collider), and the given rigidbody (the RB that the wheel-collider will apply forces to)<para/>
        /// -Both- must be valid references (i.e. cannot be null)
        /// </summary>
        /// <param name="wheel"></param>
        /// <param name="rigidBody"></param>
        public KSPWheelCollider(GameObject wheel, Rigidbody rigidBody)
        {
            this.wheel = wheel;
            if (wheel == null) { throw new NullReferenceException("Wheel game object for WheelCollider may not be null!"); }
            this.rigidBody = rigidBody;
            if (rigidBody == null) { throw new NullReferenceException("Rigidbody for wheel collider may not be null!"); }
            //default friction curves; may be set to custom curves through the get/set methods below
            sideFrictionCurve = new KSPWheelFrictionCurve(0.06f, 1.2f, 0.08f, 1.0f, 0.65f);
            fwdFrictionCurve = new KSPWheelFrictionCurve(0.06f, 1.2f, 0.08f, 1.0f, 0.65f);
        }

        /// <summary>
        /// Get/Set the current spring stiffness value.  This is the configurable value that influences the 'springForce' used in suspension calculations
        /// </summary>
        public float spring
        {
            get { return currentSpring; }
            set { currentSpring = value; }
        }

        /// <summary>
        /// Get/Set the current damper resistance value.  This is the configurable value that influences the 'dampForce' used in suspension calculations
        /// </summary>
        public float damper
        {
            get { return currentDamper; }
            set { currentDamper = value; }
        }

        /// <summary>
        /// Get/Set the current length of the suspension.  This is a ray that extends from the bottom of the wheel as positioned at the wheel collider
        /// </summary>
        public float length
        {
            get { return currentSuspensionLength; }
            set { currentSuspensionLength = value; }
        }

        /// <summary>
        /// Get/Set the current target value.  This is a 0-1 value that determines how far up the suspension the wheel should be kept. Below this point there is no spring force, only damper forces.
        /// </summary>
        public float target
        {
            get { return currentSuspensionTarget; }
            set { currentSuspensionTarget = value; }
        }

        /// <summary>
        /// Get/Set the current wheel mass.  This determines wheel acceleration from torque (not vehicle acceleration; that is determined by down-force).  Lighter wheels will slip easier from brake and motor torque.
        /// </summary>
        public float mass
        {
            get { return currentWheelMass; }
            set
            {
                currentWheelMass = value;
                currentMomentOfInertia = currentWheelMass * currentWheelRadius * currentWheelRadius * 0.5f;
                inertiaInverse = 1.0f / currentMomentOfInertia;
                massInverse = 1.0f / currentWheelMass;
            }
        }

        /// <summary>
        /// Get/Set the wheel radius.  This determines the simulated size of the wheel, and along with mass determines the wheel moment-of-inertia which plays into wheel acceleration
        /// </summary>
        public float radius
        {
            get { return currentWheelRadius; }
            set
            {
                currentWheelRadius = value;
                currentMomentOfInertia = currentWheelMass * currentWheelRadius * currentWheelRadius * 0.5f;
                inertiaInverse = 1.0f / currentMomentOfInertia;
                radiusInverse = 1.0f / currentWheelRadius;
            }
        }

        /// <summary>
        /// Get/Set the current forward friction curve.  This determines the maximum available traction force for a given slip ratio.  See the KSPWheelFrictionCurve class for more info.
        /// </summary>
        public KSPWheelFrictionCurve forwardFrictionCurve
        {
            get { return fwdFrictionCurve; }
            set { if (value != null) { fwdFrictionCurve = value; } }
        }

        /// <summary>
        /// Get/Set the current sideways friction curve.  This determines the maximum available traction force for a given slip ratio.  See the KSPWheelFrictionCurve class for more info.
        /// </summary>
        public KSPWheelFrictionCurve sidewaysFrictionCurve
        {
            get { return sideFrictionCurve; }
            set { if (value != null) { sideFrictionCurve = value; } }
        }

        /// <summary>
        /// Get/set the current forward friction coefficient; this is a direct multiple to the maximum available traction/force from forward friction<para/>
        /// Higher values denote more friction, greater traction, and less slip
        /// </summary>
        public float forwardFrictionCoefficient
        {
            get { return currentFwdFrictionCoef; }
            set { currentFwdFrictionCoef = value; }
        }

        /// <summary>
        /// Get/set the current sideways friction coefficient; this is a direct multiple to the maximum available traction/force from sideways friction<para/>
        /// Higher values denote more friction, greater traction, and less slip
        /// </summary>
        public float sideFrictionCoefficient
        {
            get { return currentSideFrictionCoef; }
            set { currentSideFrictionCoef = value; }
        }

        /// <summary>
        /// Get/set the current surface friction coefficient; this is a direct multiple to the maximum available traction for both forwards and sideways friction calculations<para/>
        /// Higher values denote more friction, greater traction, and less slip
        /// </summary>
        public float surfaceFrictionCoefficient
        {
            get { return currentSurfaceFrictionCoef; }
            set { currentSurfaceFrictionCoef = value; }
        }

        /// <summary>
        /// Get/set the actual brake torque to be used for wheel velocity update/calculations.  Should always be a positive value; sign of the value will be determined dynamically. <para/>
        /// Any braking-response speed should be calculated in the external module before setting this value.
        /// </summary>
        public float brakeTorque
        {
            get { return currentBrakeTorque; }
            set { currentBrakeTorque = Mathf.Abs(value); }
        }

        /// <summary>
        /// Get/set the current motor torque value to be applied to the wheels.  Can be negative for reversable motors / reversed wheels.<para/>
        /// Any throttle-response/etc should be calculated in the external module before setting this value.
        /// </summary>
        public float motorTorque
        {
            get { return currentMotorTorque; }
            set { currentMotorTorque = value; }
        }

        /// <summary>
        /// Get/set the current steering angle to be used by wheel friction code.<para/>
        /// Any steering-response speed should be calculated in the external module before setting this value.
        /// </summary>
        public float steeringAngle
        {
            get { return currentSteerAngle; }
            set { currentSteerAngle = value; }
        }

        /// <summary>
        /// Return true/false if tire was grounded on the last suspension check
        /// </summary>
        public bool isGrounded
        {
            get { return grounded; }
        }

        /// <summary>
        /// Wheel rotation in revloutions per minute, linked to angular velocity (changing one changes the other)
        /// </summary>
        public float rpm
        {
            // wWheel / (pi*2) * 60f
            // all values converted to combined constants
            get { return currentAngularVelocity * 9.549296585f; }
            set { currentAngularVelocity = value * 0.104719755f; }
        }

        /// <summary>
        /// angular velocity in radians per second, linked to rpm (changing one changes the other)
        /// </summary>
        public float angularVelocity
        {
            get { return currentAngularVelocity; }
            set { currentAngularVelocity = value; }
        }

        /// <summary>
        /// compression distance of the suspension system; 0 = max droop, max = max suspension length
        /// </summary>
        public float compressionDistance
        {
            get { return currentSuspensionCompression; }
        }

        /// <summary>
        /// Seat the reference to the wheel-impact callback.  This method will be called when the wheel first contacts the surface, passing in the wheel-local impact velocity (impact force is unknown)
        /// </summary>
        /// <param name="callback"></param>
        public void setImpactCallback(Action<Vector3> callback)
        {
            onImpactCallback = callback;
        }

        /// <summary>
        /// Get/Set the current raycast layer mask to be used by the wheel-collider ray/sphere-casting.<para/>
        /// This determines which colliders will be checked against for suspension positioning/spring force calculation.
        /// </summary>
        public int raycastMask
        {
            get { return currentRaycastMask; }
            set { currentRaycastMask = value; }
        }

        /// <summary>
        /// Return the per-render-frame rotation for the wheel mesh<para/>
        /// this value can be used such as wheelMeshObject.transform.Rotate(Vector3.right, getWheelFrameRotation(), Space.Self)
        /// </summary>
        /// <returns></returns>
        public float perFrameRotation
        {
            // returns rpm * 0.16666_ * 360f * secondsPerFrame
            // degrees per frame = (rpm / 60) * 360 * secondsPerFrame
            get { return rpm * 6 * Time.deltaTime; }
        }

        /// <summary>
        /// Returns the last calculated value for spring force, in newtons; this is the force that is exerted on rigidoby along suspension axis<para/>
        /// This already has dampForce applied to it; for raw spring force = springForce-dampForce
        /// </summary>
        public float springForce
        {
            get { return localForce.y; }
        }

        /// <summary>
        /// Returns the last calculated value for damper force, in newtons
        /// </summary>
        public float dampForce
        {
            get { return fDamp; }
        }

        /// <summary>
        /// Returns the last calculated longitudinal (forwards) force exerted by the wheel on the rigidbody
        /// </summary>
        public float longitudinalForce
        {
            get { return localForce.z; }
        }

        /// <summary>
        /// Returns the last calculated lateral (sideways) force exerted by the wheel on the rigidbody
        /// </summary>
        public float lateralForce
        {
            get { return localForce.x; }
        }

        /// <summary>
        /// Returns the last caclulated longitudinal slip ratio; this is basically (vWheelDelta-vLong)/vLong with some error checking, clamped to a 0-1 value; does not infer slip direction, merely the ratio
        /// </summary>
        public float longitudinalSlip
        {
            get { return sLong; }
        }

        /// <summary>
        /// Returns the last caclulated lateral slip ratio; this is basically vLat/vLong with some error checking, clamped to a 0-1 value; does not infer slip direction, merely the ratio
        /// </summary>
        public float lateralSlip
        {
            get { return sLat; }
        }

        public Vector3 wheelLocalVelocity
        {
            get { return localVelocity; }
        }

        public Collider contactColliderHit
        {
            get { return hitCollider; }
        }

        public Vector3 contactNormal
        {
            get { return hitNormal; }
        }

        public KSPWheelSweepType sweepType
        {
            get { return this.currentSweepType; }
            set { currentSweepType = value; }
        }

        public KSPWheelFrictionType frictionModel
        {
            get { return currentFrictionModel; }
            set { currentFrictionModel = value; }
        }

        /// <summary>
        /// UpdateWheel() should be called by the controlling component/container on every FixedUpdate that this wheel should apply forces for.<para/>
        /// Collider and physics integration can be disabled by simply no longer calling UpdateWheel
        /// </summary>
        public void updateWheel()
        {
            wheelForward = Quaternion.AngleAxis(currentSteerAngle, wheel.transform.up) * wheel.transform.forward;
            wheelUp = wheel.transform.up;
            wheelRight = -Vector3.Cross(wheelForward, wheelUp);
            prevSuspensionCompression = currentSuspensionCompression;
            prevFSpring = localForce.y;
            float prevVSpring = vSpring;
            bool prevGrounded = grounded;
            if (checkSuspensionContact())//suspension compression is calculated in the suspension contact check
            {
                //surprisingly, this seems to work extremely well...
                //there will be the 'undefined' case where hitNormal==wheelForward (hitting a vertical wall)
                //but that collision would never be detected anyway, as well as the suspension force would be undefined/uncalculated
                wR = Vector3.Cross(hitNormal, wheelForward);
                wF = -Vector3.Cross(hitNormal, wR);

                wF = wheelForward - hitNormal * Vector3.Dot(wheelForward, hitNormal);
                wR = Vector3.Cross(hitNormal, wF);
                //wR = wheelRight - hitNormal * Vector3.Dot(wheelRight, hitNormal);
                

                //no idea if this is 'proper' for transforming velocity from world-space to wheel-space; but it seems to return the right results
                //the 'other' way to do it would be to construct a quaternion for the wheel-space rotation transform and multiple
                // vqLocal = qRotation * vqWorld * qRotationInverse;
                // where vqWorld is a quaternion with a vector component of the world velocity and w==0
                // the output being a quaternion with vector component of the local velocity and w==0
                Vector3 worldVelocityAtHit = rigidBody.GetPointVelocity(hitPoint);
                float mag = worldVelocityAtHit.magnitude;
                localVelocity.z = Vector3.Dot(worldVelocityAtHit.normalized, wF) * mag;
                localVelocity.x = Vector3.Dot(worldVelocityAtHit.normalized, wR) * mag;
                localVelocity.y = Vector3.Dot(worldVelocityAtHit.normalized, hitNormal) * mag;

                calcSpring();
                integrateForces();
                if (!prevGrounded && onImpactCallback != null)//if was not previously grounded, call-back with impact data; we really only know the impact velocity
                {
                    onImpactCallback.Invoke(localVelocity);
                }
            }
            else
            {
                integrateUngroundedTorques();
                grounded = false;
                vSpring = prevVSpring = prevFSpring = fDamp = prevSuspensionCompression = currentSuspensionCompression = 0;
                localForce = Vector3.zero;
                hitNormal = Vector3.zero;
                hitPoint = Vector3.zero;
                hitCollider = null;
                localVelocity = Vector3.zero;
            }
            updateStickyJoint();
        }

        #endregion ENDREGION - Public accessible methods, API get/set methods

        #region REGION - Private/internal update methods

        /// <summary>
        /// Integrate the torques and forces for a grounded wheel, using the pre-calculated fSpring downforce value.
        /// </summary>
        private void integrateForces()
        {
            calcFriction();
            //no clue if this is correct or not, but does seem to clean up some suspension force application problems at high incident angles
            float suspensionDot = Vector3.Dot(hitNormal, wheelUp);

            Vector3 calculatedForces = hitNormal * localForce.y;// * suspensionDot;
            calculatedForces += localForce.z * wF;
            calculatedForces += localForce.x * wR;
            rigidBody.AddForceAtPosition(calculatedForces, hitPoint, ForceMode.Force);
            if (hitCollider.attachedRigidbody != null && !hitCollider.attachedRigidbody.isKinematic)
            {
                hitCollider.attachedRigidbody.AddForceAtPosition(-calculatedForces, hitPoint, ForceMode.Force);
            }
        }

        /// <summary>
        /// Integrate drive and brake torques into wheel velocity for when -not- grounded.
        /// This allows for wheels to change velocity from user input while the vehicle is not in contact with the surface.
        /// Not-yet-implemented are torques on the rigidbody due to wheel accelerations.
        /// </summary>
        private void integrateUngroundedTorques()
        {
            //velocity change due to motor; if brakes are engaged they can cancel this out the same tick
            //acceleration is in radians/second; only operating on fixedDeltaTime seconds, so only update for that length of time
            currentAngularVelocity += currentMotorTorque * inertiaInverse * Time.fixedDeltaTime;
            // maximum torque exerted by brakes onto wheel this frame
            float wBrake = currentBrakeTorque * inertiaInverse * Time.fixedDeltaTime;
            // clamp the max brake angular change to the current angular velocity
            wBrake = Mathf.Min(Mathf.Abs(currentAngularVelocity), wBrake);
            // sign it opposite of current wheel spin direction
            // and finally, integrate it into wheel angular velocity
            currentAngularVelocity += wBrake * -Mathf.Sign(currentAngularVelocity);
        }

        private ConfigurableJoint bumpStopJoint;
        private GameObject hitPointObject;
        private Rigidbody hitPointRigidbody;

        /// <summary>
        /// Per-fixed-update configuration of the rigidbody joints that are used for sticky friction and anti-punchthrough behaviour
        /// </summary>
        /// <param name="fwd"></param>
        /// <param name="side"></param>
        private void updateStickyJoint()
        {
            //if (bumpStopJoint == null)
            //{
            //    hitPointObject = new GameObject("HIT");
            //    hitPointRigidbody = hitPointObject.AddComponent<Rigidbody>();
            //    hitPointRigidbody.isKinematic = true;
            //    hitPointRigidbody.mass = 1f;

            //    bumpStopJoint = rigidBody.gameObject.AddComponent<ConfigurableJoint>();
            //    bumpStopJoint.anchor = wheel.transform.localPosition;// - (currentSuspenionLength + currentWheelRadius) * Vector3.up;
            //    bumpStopJoint.axis = Vector3.right;
            //    bumpStopJoint.connectedBody = hitPointRigidbody;
            //    bumpStopJoint.autoConfigureConnectedAnchor = false;
            //    bumpStopJoint.secondaryAxis = Vector3.up;
            //    bumpStopJoint.targetPosition = -Vector3.up * (currentWheelRadius*1.0125f);

            //    //SoftJointLimitSpring bumpStopLimitSpring = new SoftJointLimitSpring();
            //    //bumpStopLimitSpring.spring = 0;
            //    //bumpStopLimitSpring.damper = 0;
            //    //bumpStopJoint.linearLimitSpring = bumpStopLimitSpring;

            //    SoftJointLimit bumpStopLimit = new SoftJointLimit();
            //    bumpStopLimit.bounciness = 0;
            //    bumpStopLimit.limit = currentSuspensionLength + currentWheelRadius*1.5f;
            //    bumpStopLimit.contactDistance = 0f;
            //    bumpStopJoint.linearLimit = bumpStopLimit;

            //    JointDrive YD = new JointDrive();
            //    YD.positionSpring = 200000;
            //    YD.positionDamper = 0;
            //    YD.maximumForce = 10000000;
            //    YD.mode = JointDriveMode.Position;
            //    bumpStopJoint.yDrive = YD;                
            //}
            //hitPointObject.transform.position = wheel.transform.position - wheelUp * (currentSuspensionLength - currentSuspensionCompression + currentWheelRadius);
            //bumpStopJoint.connectedAnchor = Vector3.zero;
            //if (grounded && currentSuspensionCompression > currentSuspensionLength)
            //{
            //    bumpStopJoint.yMotion = ConfigurableJointMotion.Limited;
            //}
            //else
            //{
            //    bumpStopJoint.yMotion = ConfigurableJointMotion.Free;
            //}
            

            //if (grounded)
            //{
            //}
            //else
            //{
            //    
            //}

            //if (stickyJoint == null)
            //{
            //    stickyJoint = rigidBody.gameObject.AddComponent<ConfigurableJoint>();
            //    stickyJoint.anchor = wheel.transform.localPosition;
            //    stickyJoint.axis = Vector3.right;
            //    stickyJoint.autoConfigureConnectedAnchor = false;
            //    stickyJoint.secondaryAxis = Vector3.up;
            //}

            // this will either be the contact point as seen by the wheel
            // or.. some arbitrary point in space at the bottom of the wheels droop

            //stickyJoint.connectedAnchor = wheel.transform.position - wheelUp * ((currentSuspenionLength - currentSuspensionCompression) + currentWheelRadius);
            //if (grounded && Math.Abs(localVelocity.z) < maxStickyVelocity && currentMotorTorque == 0)
            //{
            //    fwdStickyTimer += Time.fixedDeltaTime;
            //}
            //else
            //{
            //    fwdStickyTimer = 0;
            //}

            //if (grounded && Math.Abs(localVelocity.x) < maxStickyVelocity && Mathf.Abs(localForce.x) < springForce * 0.1f)
            //{
            //    sideStickyTimer +=Time.fixedDeltaTime;
            //}
            //else
            //{
            //    sideStickyTimer = 0;
            //}
            //if (fwdStickyTimer >= fwdStickyTimeMax)
            //{
            //    stickyJoint.zMotion = ConfigurableJointMotion.Locked;
            //}
            //else
            //{
            //    stickyJoint.zMotion = ConfigurableJointMotion.Free;
            //}
            //if (sideStickyTimer >= sideStickyTimeMax)
            //{
            //    stickyJoint.xMotion = ConfigurableJointMotion.Locked;
            //}
            //else
            //{
            //    stickyJoint.xMotion = ConfigurableJointMotion.Free;
            //}
        }

        /// <summary>
        /// Uses either ray- or sphere-cast to check for suspension contact with the ground, calculates current suspension compression, and caches the world-velocity at the contact point
        /// </summary>
        /// <returns></returns>
        private bool checkSuspensionContact()
        {
            switch (currentSweepType)
            {
                case KSPWheelSweepType.RAY:
                    return suspensionSweepRaycast();
                case KSPWheelSweepType.SPHERE:
                    return suspensionSweepSpherecast();
                case KSPWheelSweepType.CAPSULE:
                    return suspensionSweepCapsuleCast();
                default:
                    return suspensionSweepRaycast();
            }
        }

        /// <summary>
        /// Check suspension contact using a ray-cast; return true/false for if contact was detected
        /// </summary>
        /// <returns></returns>
        private bool suspensionSweepRaycast()
        {
            RaycastHit hit;
            if (Physics.Raycast(wheel.transform.position, -wheel.transform.up, out hit, currentSuspensionLength + currentWheelRadius, currentRaycastMask))
            {
                currentSuspensionCompression = currentSuspensionLength + currentWheelRadius - hit.distance;
                hitNormal = hit.normal;
                hitCollider = hit.collider;
                hitPoint = hit.point;
                grounded = true;
                return true;
            }
            grounded = false;
            return false;            
        }

        /// <summary>
        /// Check suspension contact using a sphere-cast; return true/false for if contact was detected.
        /// </summary>
        /// <returns></returns>
        private bool suspensionSweepSpherecast()
        {
            RaycastHit hit;
            //need to start cast above max-compression point, to allow for catching the case of @ bump-stop
            float rayOffset = currentWheelRadius;
            if (Physics.SphereCast(wheel.transform.position + wheel.transform.up * rayOffset, radius, -wheel.transform.up, out hit, length + rayOffset, currentRaycastMask))
            {
                currentSuspensionCompression = length + rayOffset - hit.distance;
                hitNormal = hit.normal;
                hitCollider = hit.collider;
                hitPoint = hit.point;
                grounded = true;
                return true;
            }
            grounded = false;
            return false;
        }

        //TODO config specified 'wheel width'
        //TODO config specified number of capsules
        /// <summary>
        /// less efficient and less optimal solution for skinny wheels, but avoids the edge cases caused by sphere colliders<para/>
        /// uses 2 capsule-casts in a V shape downward for the wheel instead of a sphere; 
        /// for some collisions the wheel may push into the surface slightly, up to about 1/3 radius.  
        /// Could be expanded to use more capsules at the cost of performance, but at increased collision fidelity, by simulating more 'edges' of a n-gon circle.  
        /// Sadly, unity lacks a collider-sweep function, or this could be a bit more efficient.
        /// </summary>
        /// <returns></returns>
        private bool suspensionSweepCapsuleCast()
        {
            //create two capsule casts in a v-shape
            //take whichever collides first
            float wheelWidth = 0.3f;
            float capRadius = wheelWidth * 0.5f;

            RaycastHit hit;
            RaycastHit hit1;
            RaycastHit hit2;
            bool hit1b;
            bool hit2b;
            Vector3 startPos = wheel.transform.position;
            float rayOffset = currentWheelRadius;
            float rayLength = currentSuspensionLength + rayOffset;
            float capLen = currentWheelRadius - capRadius;
            Vector3 worldOffset = wheel.transform.up * rayOffset;//offset it above the wheel by a small amount, in case of hitting bump-stop
            Vector3 capEnd1 = wheel.transform.position + wheel.transform.forward * capLen;
            Vector3 capEnd2 = wheel.transform.position - wheel.transform.forward * capLen;
            Vector3 capBottom = wheel.transform.position - wheel.transform.up * capLen;
            hit1b = Physics.CapsuleCast(capEnd1 + worldOffset, capBottom + worldOffset, capRadius, -wheel.transform.up, out hit1, rayLength, currentRaycastMask);
            hit2b = Physics.CapsuleCast(capEnd2 + worldOffset, capBottom + worldOffset, capRadius, -wheel.transform.up, out hit2, rayLength, currentRaycastMask);
            if (hit1b || hit2b)
            {
                if (hit1b && hit2b) { hit = hit1.distance < hit2.distance ? hit1 : hit2; }
                else if (hit1b) { hit = hit1; }
                else if (hit2b) { hit = hit2; }
                else
                {
                    hit = hit1;
                }
                currentSuspensionCompression = currentSuspensionLength + rayOffset - hit.distance;
                hitNormal = hit.normal;
                hitCollider = hit.collider;
                hitPoint = hit.point;
                grounded = true;
                return true;
            }
            grounded = false;
            return false;
        }

        #region REGION - Friction model shared functions

        private void calcSpring()
        {
            //calculate damper force from the current compression velocity of the spring; damp force can be negative
            vSpring = (currentSuspensionCompression - prevSuspensionCompression) / Time.fixedDeltaTime;//per second velocity
            fDamp = currentDamper * vSpring;
            //calculate spring force basically from displacement * spring
            float fSpring = (currentSuspensionCompression - (currentSuspensionLength * currentSuspensionTarget)) * currentSpring;
            //if spring would be negative at this point, zero it to allow the damper to still function; this normally occurs when target > 0, at the lower end of wheel droop below target position
            if (fSpring < 0) { fSpring = 0; }
            //integrate damper value into spring force
            fSpring += fDamp;
            //if final spring value is negative, zero it out; negative springs are not possible without attachment to the ground; gravity is our negative spring :)
            if (fSpring < 0) { fSpring = 0; }
            localForce.y = fSpring;
        }

        private void calcFriction()
        {
            switch (currentFrictionModel)
            {
                case KSPWheelFrictionType.STANDARD:
                    calcFrictionStandard();
                    break;
                case KSPWheelFrictionType.PACEJKA:
                    calcFrictionPacejka();
                    break;
                case KSPWheelFrictionType.PHSYX:
                    calcFrictionPhysx();
                    break;
                default:
                    calcFrictionStandard();
                    break;
            }
        }

        /// <summary>
        /// Returns a slip ratio between 0 and 1, 0 being no slip, 1 being lots of slip
        /// </summary>
        /// <param name="vLong"></param>
        /// <param name="vWheel"></param>
        /// <returns></returns>
        private float calcLongSlip(float vLong, float vWheel)
        {
            float sLong = 0;
            if(vLong==0 && vWheel == 0) { return 0f; }//no slip present
            float a = Mathf.Max(vLong, vWheel);
            float b = Mathf.Min(vLong, vWheel);
            sLong = (a - b) / Mathf.Abs(a);
            sLong = Mathf.Clamp(sLong, 0, 1);
            return sLong;
        }

        /// <summary>
        /// Returns a slip ratio between 0 and 1, 0 being no slip, 1 being lots of slip
        /// </summary>
        /// <param name="vLong"></param>
        /// <param name="vLat"></param>
        /// <returns></returns>
        private float calcLatSlip(float vLong, float vLat)
        {
            float sLat = 0;
            if (vLat == 0)//vLat = 0, so there can be no sideways slip
            {
                return 0f;
            }
            else if (vLong == 0)//vLat!=0, but vLong==0, so all slip is sideways
            {
                return 1f;
            }
            sLat = Mathf.Abs(Mathf.Atan(vLat / vLong));//radians
            sLat = sLat * Mathf.Rad2Deg;//degrees
            sLat = sLat / 90f;//percentage (0 - 1)
            return sLat;
        }

        #endregion ENDREGION - Friction calculations methods based on alternate

        #region REGION - Standard Friction Model
        // based on : http://www.asawicki.info/Mirror/Car%20Physics%20for%20Games/Car%20Physics%20for%20Games.html

        public void calcFrictionStandard()
        {
            //initial motor/brake torque integration, brakes integrated further after friction applied
            //motor torque applied directly
            currentAngularVelocity += currentMotorTorque * inertiaInverse * Time.fixedDeltaTime;//acceleration is in radians/second; only operating on 1 * fixedDeltaTime seconds, so only update for that length of time
            // maximum torque exerted by brakes onto wheel this frame
            float wBrakeMax = currentBrakeTorque * inertiaInverse * Time.fixedDeltaTime;
            // clamp the max brake angular change to the current angular velocity
            float wBrake = Mathf.Min(Mathf.Abs(currentAngularVelocity), wBrakeMax);
            // sign it opposite of current wheel spin direction
            // and finally, integrate it into wheel angular velocity
            currentAngularVelocity += wBrake * -Mathf.Sign(currentAngularVelocity);
            // this is the remaining brake angular acceleration/torque that can be used to counteract wheel acceleration caused by traction friction
            float wBrakeDelta = wBrakeMax - wBrake;
            
            vWheel = currentAngularVelocity * currentWheelRadius;
            sLong = calcLongSlip(localVelocity.z, vWheel);
            sLat = calcLatSlip(localVelocity.z, localVelocity.x);
            vWheelDelta = vWheel - localVelocity.z;

            float fLongMax = fwdFrictionCurve.evaluate(sLong) * localForce.y * currentFwdFrictionCoef * currentSurfaceFrictionCoef;
            float fLatMax = sideFrictionCurve.evaluate(sLat) * localForce.y * currentSideFrictionCoef * currentSurfaceFrictionCoef;

            // TODO - this should actually be limited by the amount of force necessary to arrest the velocity of this wheel in this frame
            // so limit max should be (abs(vLat) * sprungMass) / Time.fixedDeltaTime  (in newtons)
            localForce.x = fLatMax;
            // using current down-force as a 'sprung-mass' to attempt to limit overshoot when bringing the velocity to zero; the 2x multiplier is just because it helped with response but didn't induce oscillations; higher multipliers can
            if (localForce.x > Mathf.Abs(localVelocity.x) * localForce.y * 2f) { localForce.x = Mathf.Abs(localVelocity.x) * localForce.y * 2f; }
            // if (fLat > sprungMass * Mathf.Abs(vLat) / Time.fixedDeltaTime) { fLat = sprungMass * Mathf.Abs(vLat) * Time.fixedDeltaTime; }
            localForce.x *= -Mathf.Sign(localVelocity.x);// sign it opposite to the current vLat

            //angular velocity delta between wheel and surface in radians per second; radius inverse used to avoid div operations
            float wDelta = vWheelDelta * radiusInverse;
            //amount of torque needed to bring wheel to surface speed over one second
            float tDelta = wDelta * currentMomentOfInertia;
            //newtons of force needed to bring wheel to surface speed over one second; radius inverse used to avoid div operations
            // float fDelta = tDelta * radiusInverse; // unused
            //absolute value of the torque needed to bring the wheel to road speed instantaneously/this frame
            float tTractMax = Mathf.Abs(tDelta) / Time.fixedDeltaTime;
            //newtons needed to bring wheel to ground velocity this frame; radius inverse used to avoid div operations
            float fTractMax = tTractMax * radiusInverse;
            //final maximum force value is the smallest of the two force values;
            // if fTractMax is used the wheel will be brought to surface velocity,
            // otherwise fLongMax is used and the wheel is still slipping but maximum traction force will be exerted
            fTractMax = Mathf.Min(fTractMax, fLongMax);
            // convert the clamped traction value into a torque value and apply to the wheel
            float tractionTorque = fTractMax * currentWheelRadius * -Mathf.Sign(vWheelDelta);
            // and set the longitudinal force to the force calculated for the wheel/surface torque
            localForce.z = fTractMax * Mathf.Sign(vWheelDelta);
            //use wheel inertia to determine final wheel acceleration from torques; inertia inverse used to avoid div operations; convert to delta-time, as accel is normally radians/s
            float angularAcceleration = tractionTorque * inertiaInverse * Time.fixedDeltaTime;
            //apply acceleration to wheel angular velocity
            currentAngularVelocity += angularAcceleration;
            //second integration pass of brakes, to allow for locked-wheels after friction calculation
            if (Mathf.Abs(currentAngularVelocity) < wBrakeDelta)
            {
                currentAngularVelocity = 0;
                wBrakeDelta -= Mathf.Abs(currentAngularVelocity);
                float fMax = Mathf.Max(0, Mathf.Abs(fLongMax) - Mathf.Abs(localForce.z));//remaining 'max' traction left
                float fMax2 = Mathf.Max(0, localForce.y * Mathf.Abs(localVelocity.z) * 2 - Mathf.Abs(localForce.z));
                float fBrakeMax = Mathf.Min(fMax, fMax2);
                localForce.z += fBrakeMax * -Mathf.Sign(localVelocity.z);
            }
            else
            {
                currentAngularVelocity += -Mathf.Sign(currentAngularVelocity) * wBrakeDelta;//traction from this will be applied next frame from wheel slip, but we're integrating here basically for rendering purposes
            }

            // cap friction / combined friction
            // in this simplified model, longitudinal force wins
            float cap = Mathf.Max(fLatMax, fLongMax);
            float latLimit = cap - Mathf.Abs(localForce.z);
            if (Mathf.Abs(localForce.x) > latLimit) { localForce.x = latLimit * Mathf.Sign(localForce.x); }
        }

        #endregion ENDREGION - Standard Friction Model

        #region REGION - Alternate Friction Model - Pacejka
        // based on http://www.racer.nl/reference/pacejka.htm
        // and also http://www.mathworks.com/help/physmod/sdl/ref/tireroadinteractionmagicformula.html?requestedDomain=es.mathworks.com
        // and http://www.edy.es/dev/docs/pacejka-94-parameters-explained-a-comprehensive-guide/
        // and http://www.edy.es/dev/2011/12/facts-and-myths-on-the-pacejka-curves/
        // and http://www-cdr.stanford.edu/dynamic/bywire/tires.pdf

        public void calcFrictionPacejka()
        {
            // TODO
            // really this should just be an adjustment to the curve parameters
            // as all that the pacejka formulas do is define the curves used by slip ratio to calculate maximum force output
            
            vWheel = currentAngularVelocity * currentWheelRadius;
            sLong = calcLongSlip(localVelocity.z, vWheel);
            sLat = calcLatSlip(localVelocity.z, localVelocity.x);
            vWheelDelta = vWheel - localVelocity.z;

            // 'simple' magic-formula
            float B = 10f;//stiffness
            // float C = 1.9f;
            float Clat = 1.3f;
            float Clong = 1.65f;
            float D = 1;
            float E = 0.97f;
            // F = Fz * D * sin(C * atan(B*slip - E * (B*slip - atan(B*slip))))
            float Fz = localForce.y;
            float slipLat = sLat * 100f;
            float slipLong = sLong * 100f;
            float fLatMax = localForce.x = Fz * D * Mathf.Sin(Clat * Mathf.Atan(B * slipLat - E * (B * slipLat - Mathf.Atan(B * slipLat))));
            float fLongMax = localForce.z = Fz * D * Mathf.Sin(Clong * Mathf.Atan(B * slipLong - E * (B * slipLong - Mathf.Atan(B * slipLong))));

            if (localForce.x > Mathf.Abs(localVelocity.x) * localForce.y * 2f) { localForce.x = Mathf.Abs(localVelocity.x) * localForce.y * 2f; }
            localForce.x *= -Mathf.Sign(localVelocity.x);// sign it opposite to the current vLat
            
            //angular velocity delta between wheel and surface in radians per second; radius inverse used to avoid div operations
            float wDelta = vWheelDelta * radiusInverse;
            //amount of torque needed to bring wheel to surface speed over one second
            float tDelta = wDelta * currentMomentOfInertia;
            //newtons of force needed to bring wheel to surface speed over one update tick
            float fDelta = tDelta * radiusInverse / Time.fixedDeltaTime;
            localForce.z = Mathf.Min(Mathf.Abs(fDelta), localForce.z) * Mathf.Sign(fDelta);
            float tTract = -localForce.z * currentWheelRadius;
            currentAngularVelocity += tTract * Time.fixedDeltaTime * inertiaInverse;
            currentAngularVelocity += currentMotorTorque * Time.fixedDeltaTime * inertiaInverse;
            
            float cap = Mathf.Max(fLatMax, fLongMax);
            float latLimit = cap - Mathf.Abs(localForce.z);
            if (Mathf.Abs(localForce.x) > latLimit) { localForce.x = latLimit * Mathf.Sign(localForce.x); }
        }

        #endregion ENDREGION - Alternate friction model

        #region REGION - Alternate Friction Model - PhysX

        // TODO
        // based on http://www.eggert.highpeakpress.com/ME485/Docs/CarSimEd.pdf
        public void calcFrictionPhysx()
        {
            calcFrictionStandard();
        }

        #endregion ENDREGION - Alternate Friction Model 2

        public void drawDebug()
        {
            if (!grounded) { return; }
            
            Vector3 rayStart, rayEnd;
            Vector3 vOffset = rigidBody.velocity * Time.fixedDeltaTime;

            //draw the force-vector line
            rayStart = hitPoint;
            //because localForce isn't really a vector... its more 3 separate force-axis combinations...
            rayEnd = hitNormal * localForce.y;
            rayEnd += wR * localForce.x;
            rayEnd += wF * localForce.z;
            rayEnd += rayStart;

            //rayEnd = rayStart + wheel.transform.TransformVector(localForce.normalized) * 2f;
            Debug.DrawLine(rayStart + vOffset, rayEnd + vOffset, Color.magenta);

            rayStart += wheel.transform.up * 0.1f;
            rayEnd = rayStart + wF * 10f;
            Debug.DrawLine(rayStart + vOffset, rayEnd + vOffset, Color.blue);

            rayEnd = rayStart + wR * 10f;
            Debug.DrawLine(rayStart + vOffset, rayEnd + vOffset, Color.red);
        }

        #endregion ENDREGION - Private/internal update methods

    }

    public enum KSPWheelSweepType
    {
        RAY,
        SPHERE,
        CAPSULE
    }

    public enum KSPWheelFrictionType
    {
        STANDARD,
        PACEJKA,
        PHSYX
    }

}
