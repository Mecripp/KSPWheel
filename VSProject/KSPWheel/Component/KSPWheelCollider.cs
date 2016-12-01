﻿using System;
using UnityEngine;

namespace KSPWheel
{
    public class KSPWheelCollider : MonoBehaviour
    {

        #region REGION - Private variables

        //private vars with either external get/set methods, or internal calculated fields from other fields get/set methods
        private GameObject wheel;
        private Rigidbody rigidBody;
        private float wheelMass = 1f;
        private float wheelRadius = 0.5f;
        private float suspensionLength = 1f;
        private float suspensionTarget = 0f;
        private float suspensionSpring = 10f;
        private float suspensionDamper = 2f;
        private float currentFwdFrictionCoef = 1f;
        private float currentSideFrictionCoef = 1f;
        private float currentSurfaceFrictionCoef = 1f;
        private float currentSteeringAngle = 0f;
        private float currentMotorTorque = 0f;
        private float currentBrakeTorque = 0f;
        private float currentMomentOfInertia = 1.0f * 0.5f * 0.5f * 0.5f;//moment of inertia of wheel; used for mass in acceleration calculations regarding wheel angular velocity.  MOI of a solid cylinder = ((m*r*r)/2)
        private int currentRaycastMask = ~(1 << 26);//default cast to all layers except 26; 1<<26 sets 26 to the layer; ~inverts all bits in the mask (26 = KSP WheelColliderIgnore layer)
        private KSPWheelFrictionType currentFrictionModel = KSPWheelFrictionType.STANDARD;
        private KSPWheelSweepType currentSweepType = KSPWheelSweepType.RAY;
        private KSPWheelFrictionCurve fwdFrictionCurve = new KSPWheelFrictionCurve(0.06f, 1.2f, 0.065f, 1.25f, 0.7f);//current forward friction curve
        private KSPWheelFrictionCurve sideFrictionCurve = new KSPWheelFrictionCurve(0.03f, 1.0f, 0.04f, 1.05f, 0.7f);//current sideways friction curve
        private bool automaticUpdates = false;
        //set from get/set method
        private Vector3 gravity = new Vector3(0, -9.81f, 0);
        //calced when the gravity vector is set
        private Vector3 gNorm = new Vector3(0, -1, 0);
        private Action<Vector3> onImpactCallback;//simple blind callback for when the wheel changes from !grounded to grounded, the input variable is the wheel-local impact velocity
        private Action<KSPWheelCollider> preUpdateCallback;//if automatic updates are enabled, this field may optionally be populated with a pre-update callback method; will be called directly prior to the wheels internal update code being processed
        private Action<KSPWheelCollider> postUpdateCallback;//if automatic updates are enabled, this field may optionally be populated with a post-update callback method; will be called directly after the wheels internal update code processing.

        //private vars with external get methods (cannot be set, for data viewing/debug purposes only)
        private bool grounded = false;

        //cached internal utility vars
        private float inertiaInverse;//cached inertia inverse used to eliminate division operations from per-tick update code
        private float radiusInverse;//cached radius inverse used to eliminate division operations from per-tick update code
        private float massInverse;//cached mass inverse used to eliminate division operations from per-tick update code

        //internal friction model values
        private float prevFSpring;
        private float currentSuspensionCompression = 0f;
        private float prevSuspensionCompression = 0f;
        private float currentAngularVelocity = 0f;//angular velocity of wheel; rotations in radians per second
        private float vSpring;//linear velocity of spring in m/s, derived from prevCompression - currentCompression along suspension axis
        private float fDamp;//force exerted by the damper this physics frame, in newtons

        //wheel axis directions are calculated each frame during update processing
        private Vector3 wF, wR;
        private Vector3 wheelUp;
        private Vector3 wheelForward;
        private Vector3 wheelRight;
        private Vector3 localVelocity;
        private Vector3 localForce;//the wheel(contact-patch?) local forces; x=lat, y=spring, z=long
        private float vWheel;
        private float vWheelDelta;
        private float sLong;
        private float sLat;
        private Vector3 hitPoint;//world-space position of contact patch
        private Vector3 hitNormal;
        private Collider hitCollider;

        //run-time references to various objects
        private ConfigurableJoint stickyJoint;//the joint used for sticky friction

        #endregion ENDREGION - Private variables

        #region REGION - Public accessible API get/set methods

        //get-set equipped field defs

        /// <summary>
        /// Get/Set the rigidbody that the WheelCollider applies forces to.  MUST be set manually after WheelCollider component is added to a GameObject.
        /// </summary>
        public Rigidbody rigidbody
        {
            get { return rigidBody; }
            set { rigidBody = value; }
        }
        
        /// <summary>
        /// Get/Set the current spring stiffness value.  This is the configurable value that influences the 'springForce' used in suspension calculations
        /// </summary>
        public float spring
        {
            get { return suspensionSpring; }
            set { suspensionSpring = value; }
        }

        /// <summary>
        /// Get/Set the current damper resistance value.  This is the configurable value that influences the 'dampForce' used in suspension calculations
        /// </summary>
        public float damper
        {
            get { return suspensionDamper; }
            set { suspensionDamper = value; }
        }

        /// <summary>
        /// Get/Set the current length of the suspension.  This is a ray that extends from the bottom of the wheel as positioned at the wheel collider
        /// </summary>
        public float length
        {
            get { return suspensionLength; }
            set { suspensionLength = value; }
        }

        /// <summary>
        /// Get/Set the current target value.  This is a 0-1 value that determines how far up the suspension the wheel should be kept. Below this point there is no spring force, only damper forces.
        /// </summary>
        public float target
        {
            get { return suspensionTarget; }
            set { suspensionTarget = value; }
        }

        /// <summary>
        /// Get/Set the current wheel mass.  This determines wheel acceleration from torque (not vehicle acceleration; that is determined by down-force).  Lighter wheels will slip easier from brake and motor torque.
        /// </summary>
        public float mass
        {
            get { return wheelMass; }
            set
            {
                wheelMass = value;
                currentMomentOfInertia = wheelMass * wheelRadius * wheelRadius * 0.5f;
                inertiaInverse = 1.0f / currentMomentOfInertia;
                massInverse = 1.0f / wheelMass;
            }
        }

        /// <summary>
        /// Get/Set the wheel radius.  This determines the simulated size of the wheel, and along with mass determines the wheel moment-of-inertia which plays into wheel acceleration
        /// </summary>
        public float radius
        {
            get { return wheelRadius; }
            set
            {
                wheelRadius = value;
                currentMomentOfInertia = wheelMass * wheelRadius * wheelRadius * 0.5f;
                inertiaInverse = 1.0f / currentMomentOfInertia;
                radiusInverse = 1.0f / wheelRadius;
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
            get { return currentSteeringAngle; }
            set { currentSteeringAngle = value; }
        }

        /// <summary>
        /// Get/Set the gravity vector that should be used during calculations.  MUST be updated every frame that gravity differs from the previous frame or undesired and inconsistent functioning will result.
        /// </summary>
        public Vector3 gravityVector
        {
            get { return gravity; }
            set { gravity = value; gNorm = gravity.normalized; }
        }
        
        /// <summary>
        /// Get/Set the suspension sweep type -- Raycast, Spherecast, or Capsulecast (enum value)
        /// </summary>
        public KSPWheelSweepType sweepType
        {
            get { return this.currentSweepType; }
            set { currentSweepType = value; }
        }

        /// <summary>
        /// Get/Set the friction model to be used -- currently only Standard is supported.
        /// </summary>
        public KSPWheelFrictionType frictionModel
        {
            get { return currentFrictionModel; }
            set { currentFrictionModel = value; }
        }

        /// <summary>
        /// Get/Set if the WheelCollider should use its own FixedUpdate function or rely on external calling of the update method.
        /// </summary>
        public bool autoUpdateEnabled
        {
            get { return automaticUpdates; }
            set { automaticUpdates = value; }
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
        /// Set the pre-update callback method to be called when automatic updates are used
        /// </summary>
        /// <param name="callback"></param>
        public void setPreUpdateCallback(Action<KSPWheelCollider> callback)
        {
            preUpdateCallback = callback;
        }

        /// <summary>
        /// Set the post-update callback method to be called when automatic updates are used
        /// </summary>
        /// <param name="callback"></param>
        public void setPostUpdateCallback(Action<KSPWheelCollider> callback)
        {
            postUpdateCallback = callback;
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

        /// <summary>
        /// Returns the last calculated wheel-local velocity (velocity of the wheel, in the wheels' frame of reference)
        /// </summary>
        public Vector3 wheelLocalVelocity
        {
            get { return localVelocity; }
        }

        /// <summary>
        /// Returns the last raycast collider hit.
        /// </summary>
        public Collider contactColliderHit
        {
            get { return hitCollider; }
        }

        /// <summary>
        /// Returns the surface normal of the raycast collider that was hit
        /// </summary>
        public Vector3 contactNormal
        {
            get { return hitNormal; }
        }
        
        #endregion ENDREGION - Public accessible methods, API get/set methods

        #region REGION - Update methods -- internal, external

        public void FixedUpdate()
        {
            if (!automaticUpdates) { return; }
            if (preUpdateCallback != null) { preUpdateCallback.Invoke(this); }
            this.updateWheel();
            if (postUpdateCallback != null) { postUpdateCallback.Invoke(this); }
        }

        /// <summary>
        /// UpdateWheel() should be called by the controlling component/container on every FixedUpdate that this wheel should apply forces for.<para/>
        /// Collider and physics integration can be disabled by simply no longer calling UpdateWheel
        /// </summary>
        public void updateWheel()
        {
            if (rigidBody == null)
            {
                //this.rigidBody = gameObject.GetComponentUpwards<Rigidbody>();
                return;
            }
            if (this.wheel == null) { this.wheel = this.gameObject; }
            wheelForward = Quaternion.AngleAxis(currentSteeringAngle, wheel.transform.up) * wheel.transform.forward;
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
        }

        #endregion ENDREGION - Update methods -- internal, external

        #region REGION - Private/internal update methods

        public float susResponse = 0f;

        private float prevSpring = 0f;
        private float prevFLong = 0f;
        private float prevFLat = 0f;

        /// <summary>
        /// Integrate the torques and forces for a grounded wheel, using the pre-calculated fSpring downforce value.
        /// </summary>
        private void integrateForces()
        {
            calcFriction();
            if (susResponse > 0)
            {
                localForce.y = Mathf.Lerp(prevSpring, localForce.y, susResponse / Time.fixedDeltaTime);
            }
            float fMult = 0.1f;
            if ((prevFLong < 0 && localForce.z > 0) || (prevFLong > 0 && localForce.z < 0))
            {
                localForce.z *= fMult;
            }
            if ((prevFLat < 0 && localForce.x > 0) || (prevFLat > 0 && localForce.x < 0))
            {
                localForce.x *= fMult;
            }
            //no clue if this is correct or not, but does seem to clean up some suspension force application problems at high incident angles
            float suspensionDot = Vector3.Dot(hitNormal, wheelUp);
            Vector3 calculatedForces = hitNormal * localForce.y * suspensionDot;
            calculatedForces += localForce.z * wF;
            calculatedForces += localForce.x * wR;
            calculatedForces += calcAG(hitNormal, localForce.y * suspensionDot);
            rigidBody.AddForceAtPosition(calculatedForces, hitPoint, ForceMode.Force);
            if (hitCollider.attachedRigidbody != null && !hitCollider.attachedRigidbody.isKinematic)
            {
                hitCollider.attachedRigidbody.AddForceAtPosition(-calculatedForces, hitPoint, ForceMode.Force);
            }
            prevSpring = localForce.y;
            prevFLong = localForce.z;
            prevFLat = localForce.x;
        }

        /// <summary>
        /// Calculate an offset to the spring force that will negate the tendency to slide down hill caused by suspension forces.
        ///   Seems to mostly work and brings drift down to sub-milimeter-per-second.
        ///   Should be combined with some sort of spring/joint/constraint to complete the sticky-friction implementation.  
        /// </summary>
        /// <param name="hitNormal"></param>
        /// <param name="springForce"></param>
        /// <returns></returns>
        private Vector3 calcAG(Vector3 hitNormal, float springForce)
        {
            Vector3 agFix = new Vector3(0, 0, 0);
            // this is the amount of suspension force that is misaligning the vehicle
            // need to push uphill by this amount to keep the rigidbody centered along suspension axis
            float gravNormDot = Vector3.Dot(hitNormal, gNorm);
            //this force should be applied in the 'uphill' direction
            float agForce = gravNormDot * springForce;
            //calculate uphill direction from hitNorm and gNorm
            // cross of the two gives the left/right of the hill
            Vector3 hitGravCross = Vector3.Cross(hitNormal, gNorm);
            // cross the left/right with the hitNorm to derive the up/down-hill direction
            Vector3 upDown = Vector3.Cross(hitGravCross, hitNormal);
            // and pray that all the rhs/lhs coordinates are correct...
            float slopeLatDot = Vector3.Dot(upDown, wR);
            agFix = agForce * slopeLatDot * wR;
            if (brakeTorque > 0) //&& Mathf.Abs(localVelocity.z) > 1f)
            {
                float slopeLongDot = Vector3.Dot(upDown, wF);
                agFix += agForce * slopeLongDot * wF;
            }
            return agFix;
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
            if (Physics.Raycast(wheel.transform.position, -wheel.transform.up, out hit, suspensionLength + wheelRadius, currentRaycastMask))
            {
                currentSuspensionCompression = suspensionLength + wheelRadius - hit.distance;
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
            float rayOffset = wheelRadius;
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
            float rayOffset = wheelRadius;
            float rayLength = suspensionLength + rayOffset;
            float capLen = wheelRadius - capRadius;
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
                currentSuspensionCompression = suspensionLength + rayOffset - hit.distance;
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
            fDamp = suspensionDamper * vSpring;
            //calculate spring force basically from displacement * spring
            float fSpring = (currentSuspensionCompression - (suspensionLength * suspensionTarget)) * suspensionSpring;
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
            
            vWheel = currentAngularVelocity * wheelRadius;
            sLong = calcLongSlip(localVelocity.z, vWheel);
            sLat = calcLatSlip(localVelocity.z, localVelocity.x);
            vWheelDelta = vWheel - localVelocity.z;

            float fLongMax = fwdFrictionCurve.evaluate(sLong) * localForce.y * currentFwdFrictionCoef * currentSurfaceFrictionCoef;
            float fLatMax = sideFrictionCurve.evaluate(sLat) * localForce.y * currentSideFrictionCoef * currentSurfaceFrictionCoef;
            // TODO - this should actually be limited by the amount of force necessary to arrest the velocity of this wheel in this frame
            // so limit max should be (abs(vLat) * sprungMass) / Time.fixedDeltaTime  (in newtons)
            localForce.x = fLatMax;
            // using current down-force as a 'sprung-mass' to attempt to limit overshoot when bringing the velocity to zero
            // TODO - may need to adjust the multiplier when arresting downward motion so as to not induce jitter, as at times it may be greater than the actual sprung mass
            float fMult = 1f;
            if (localForce.x > Mathf.Abs(localVelocity.x) * localForce.y * fMult) { localForce.x = Mathf.Abs(localVelocity.x) * localForce.y * fMult; }
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
            float tractionTorque = fTractMax * wheelRadius * -Mathf.Sign(vWheelDelta);
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
                float fMax2 = Mathf.Max(0, localForce.y * Mathf.Abs(localVelocity.z) - Mathf.Abs(localForce.z));
                float fBrakeMax = Mathf.Min(fMax, fMax2);
                localForce.z += fBrakeMax * -Mathf.Sign(localVelocity.z);
            }
            else
            {
                currentAngularVelocity += -Mathf.Sign(currentAngularVelocity) * wBrakeDelta;//traction from this will be applied next frame from wheel slip, but we're integrating here basically for rendering purposes
            }

            //combinatorialFrictionModelOne(fLatMax, fLongMax, localForce.x, localForce.z, out localForce.x, out localForce.z);
            //combinatorialFrictionModelTwo(fLatMax, fLongMax, localForce.x, localForce.z, out localForce.x, out localForce.z);
            //combinatorialFrictionModelThree(fLatMax, fLongMax, localForce.x, localForce.z, out localForce.x, out localForce.z);
            combinatorialFrictionModelFour(fLatMax, fLongMax, localForce.x, localForce.z, out localForce.x, out localForce.z);
            //TODO technically wheel angular velocity integration should not occur until after the force is capped here, otherwise things will get out of synch
        }

        /// <summary>
        /// Simple combinatorial friction model; longitudinal friction overrides lateral if it would exceed friction cap
        /// </summary>
        private void combinatorialFrictionModelOne(float latMax, float longMax, float fLat, float fLong, out float combLat, out float combLong)
        {
            // cap friction / combined friction
            // in this simplified model, longitudinal force wins
            float max = (fwdFrictionCurve.max + sideFrictionCurve.max) * 0.5f * localForce.y;
            float cap = max;// Mathf.Max(latMax, longMax);
            float latLimit = cap - Mathf.Abs(fLong);
            if (Mathf.Abs(fLat) > latLimit) { fLat = latLimit * Mathf.Sign(fLat); }
            combLat = fLat;
            combLong = fLong;
        }

        /// <summary>
        /// Slightly less-simple friction circle based integration, sadly the force with the lowest maximum has the greatest influence on the total output
        /// </summary>
        private void combinatorialFrictionModelTwo(float latMax, float longMax, float fLat, float fLong, out float combLat, out float combLong)
        {
            float absLatMax = Mathf.Abs(latMax);
            float absLongMax = Mathf.Abs(longMax);
            float cap = (absLatMax + absLongMax)/2;
            float tot = Mathf.Abs(fLat) + Mathf.Abs(fLong);
            float pct = tot / cap;
            if (pct < 1 || float.IsNaN(pct)) { pct = 1; }
            combLat = pct==0? 0 : fLat / pct;
            combLong = pct==0? 0 : fLong / pct;
        }

        /// <summary>
        /// Honestly, I played around in excel until I found something that looked right...
        /// </summary>
        private void combinatorialFrictionModelThree(float latMax, float longMax, float fLat, float fLong, out float combLat, out float combLong)
        {
            float absLatMax = Mathf.Abs(latMax);
            float absLongMax = Mathf.Abs(longMax);
            float maxLen = Mathf.Sqrt(absLatMax * absLatMax + absLongMax * absLongMax);
            float fLen = Mathf.Sqrt(fLat * fLat + fLong * fLong);
            float cap = (absLatMax + absLongMax);
            float tot = Mathf.Abs(fLat) + Mathf.Abs(fLong);
            float nR1 = maxLen==0 ? 0 : absLatMax / maxLen;
            float nR2 = maxLen==0 ? 0 : absLongMax / maxLen;
            float nR3 = Mathf.Abs(fLat) / fLen;
            float nR4 = Mathf.Abs(fLong) / fLen;
            float nR5 = nR1==0? 0 : nR3 / nR1;
            float nR6 = nR2==0? 0 : nR4 / nR2;
            combLat = fLat * nR1 * nR5;
            combLong = fLong * nR2 * nR6;
        }

        /// <summary>
        /// Simple and effective; limit their sum to the absolute maximum friction that the tire 
        /// can ever produce, as calculated by the (averaged=/) peak points of the friction curve. 
        /// This keeps the total friction output below the max of the tire while allowing the greatest range of optimal output for both lat and long friction.
        /// -Ideally- slip ratio would be brought into the calculation somewhere, but not sure how it should be used.
        /// </summary>
        private void combinatorialFrictionModelFour(float latMax, float longMax, float fLat, float fLong, out float combLat, out float combLong)
        {
            float max = (fwdFrictionCurve.max + sideFrictionCurve.max) * 0.5f * localForce.y;
            float tot = Mathf.Abs(fLat) + Mathf.Abs(fLong);
            float pct = 1f;
            if (tot > max)
            {
                pct = max / tot;
            }
            combLat = fLat * pct;
            combLong = fLong * pct;
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
            
            vWheel = currentAngularVelocity * wheelRadius;
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
            float tTract = -localForce.z * wheelRadius;
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
