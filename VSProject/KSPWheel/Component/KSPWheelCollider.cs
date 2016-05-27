﻿using System;
using UnityEngine;

namespace KSPWheel
{
    
    public class KSPWheelCollider
    {

        #region REGION - Configuration Fields
        //Component configuration fields; not adjusted by component, but could be manipulated by other scripts

        /// <summary>
        /// The game object this script should be attached to / affect
        /// </summary>
        public GameObject wheel;

        /// <summary>
        /// The rigidbody that this wheel will apply forces to and sample velocity from
        /// </summary>
        public Rigidbody rigidBody;

        /// <summary>
        /// The radius of the wheel to simulate; this is the -actual- size to simulate, not a pre-scaled value
        /// </summary>
        public float wheelRadius;

        /// <summary>
        /// The mass of the -wheel- in... kg? tons? NFC
        /// </summary>
        public float wheelMass;//used to simulate wheel rotational inertia for brakes and friction purposes

        /// <summary>
        /// The length of the suspension travel
        /// </summary>
        public float suspensionLength = 0.5f;

        /// <summary>
        /// The 'target' parameter for suspension; 0 = fully uncompressed, 1 = fully compressed
        /// </summary>
        public float target = 0;

        /// <summary>
        /// The maximum force the suspension will exhert, in newtons
        /// </summary>
        public float spring = 100;

        /// <summary>
        /// The damping ratio for the suspension spring force
        /// </summary>
        public float damper = 1;

        /// <summary>
        /// The maximum torque the motor can exhert against the wheel
        /// </summary>
        public float motorTorque = 0;

        /// <summary>
        /// The maximum torque the brakes can exhert against the wheel while attempting to bring its angular velocity to zero
        /// </summary>
        public float brakeTorque = 0;

        /// <summary>
        /// The maximum deflection for the steering of this wheel, in degrees
        /// </summary>
        public float maxSteerAngle = 0;

        /// <summary>
        /// The steering response speed; higher values result in more responsive steering
        /// </summary>
        public float steerLerpSpeed = 1;

        /// <summary>
        /// The forward friction constant (rolling friction)
        /// </summary>
        public float fwdFrictionConst = 1f;

        /// <summary>
        /// The sideways friction constant
        /// </summary>
        public float sideFrictionConst = 1f;

        #endregion ENDREGION - Configuration Fields

        #region REGION - Public Accessible derived values

        /// <summary>
        /// The pre-calculated position that the wheel mesh should be positioned at; alternatively you can calculate the position manually given the 'compressionLength' value
        /// </summary>
        public Vector3 wheelMeshPosition;

        /// <summary>
        /// The distance that the suspension is compressed
        /// </summary>
        public float compressionDistance;

        /// <summary>
        /// The percentage of compression calculated as (compressLength / suspensionLength)
        /// </summary>
        public float compressionPercent;

        /// <summary>
        /// The final calculated force being exerted by the spring; this can be used as the 'down force' of the wheel <para/>
        /// springForce = rawSpringForce - dampForce
        /// </summary>
        public float springForce;

        /// <summary>
        /// The amount of force of the spring that was negated by the damping value<para/>
        /// The 'springForce' variable already has this value applied;
        /// raw spring force (before damper) can be calculated by springForce+dampForce
        /// </summary>
        public float dampForce;

        /// <summary>
        /// The velocity of the wheel as seen at the wheel mounting point in the local reference of the wheel collider object (the object this script is attached to)<para/>
        /// This does not take into account the steering angle of the wheel
        /// </summary>
        public Vector3 wheelMountLocalVelocity;

        /// <summary>
        /// The velocity of the wheel as seen by the surface at the point of contact, taking into account steering angle and angle of the collider to the surface.
        /// </summary>
        public Vector3 wheelLocalVelocity;

        /// <summary>
        /// The velocity of the wheel in world space at the point of contact
        /// </summary>
        public Vector3 worldVelocityAtHit;

        /// <summary>
        /// The summed forces that will be/have been applied to the rigidbody at the point of contact with the surface
        /// </summary>
        public Vector3 forceToApply;

        /// <summary>
        /// If grounded == true, this is populated with a reference to the raycast hit information
        /// </summary>
        public RaycastHit hit;

        /// <summary>
        /// The current measured RPM of the wheel; derived from down-force, motor-force, wheel mass, and previous RPM
        /// </summary>
        public float wheelRPM;
        
        /// <summary>
        /// At each update set to true or false depending on if the wheel is in contact with the ground<para/>
        /// Saved persistently in the KSPWheelModule, manually restored upon instantiation of the KSPWheelCollider; this prevents erroneous callbacks on part-load.
        /// </summary>
        public bool grounded;

        #endregion ENDREGION - Public Accessible derived values

        #region REGION - Public editor-display variables
        public float prevCompressionDistance;
        public float springVelocity;
        public float compressionPercentInverse;
        public Vector3 wheelForward;
        public Vector3 wheelRight;
        public Vector3 wheelUp;
        public float currentSteerAngle;
        #endregion ENDREGION - Public editor-display variables

        #region REGION - Private working variables
        private float fwdInput = 0;
        private float rotInput = 0;
        private float brakeInput = 0;
        private int sideStickyTimer = 0;
        private int fwdStickyTimer = 0;
        private float maxStickyVelocity = 0.1f;//TODO -- expose as a configurable field
        private ConfigurableJoint stickyJoint;//the joint used for sticky friction
        private int raycastMask = ~(1 << 26);//cast to all layers except 26; 1<<26 sets 26 to the layer; ~inverts all bits in the mask
        private Action<Vector3> onImpactCallback;//simple blind callback for when the wheel changes from !grounded to grounded, the input variable is the wheel-local impact velocity 
        private KSPWheelFrictionCurve sideFrictionCurve;
        private KSPWheelFrictionCurve fwdFrictionCurve;
        #endregion ENDREGION - Private working variables

        #region REGION - more private vars; debug

        public bool debug = false;

        public float wWheel;
        public float iWheel;//moment of inertia of wheel; used for mass in acceleration calculations regarding wheel angular velocity        
        public float vLong;
        public float vLat;
        public float vWheel;
        public float sLong;
        public float sLat;
        public float fLong;
        public float fLat;

        public float tDrive;
        public float tBrake;
        public float tRoll;
        public float tTract;
        public float tTotal;

        public float wAccel;

        #endregion

        #region REGION - Public accessible methods / API methods

        public KSPWheelCollider(GameObject wheel, Rigidbody rigidBody)
        {
            this.wheel = wheel;
            this.rigidBody = rigidBody;

            sideFrictionCurve = new KSPWheelFrictionCurve();
            sideFrictionCurve.addPoint(0, 0, 0, 0);
            sideFrictionCurve.addPoint(0.06f, 1.1f, 0, 0);
            sideFrictionCurve.addPoint(0.08f, 1.0f, 0, 0);
            sideFrictionCurve.addPoint(1.00f, 0.65f, 0, 0);

            fwdFrictionCurve = new KSPWheelFrictionCurve();
            fwdFrictionCurve.addPoint(0, 0, 0, 0);
            fwdFrictionCurve.addPoint(0.06f, 1.1f, 0, 0);
            fwdFrictionCurve.addPoint(0.08f, 1.0f, 0, 0);
            fwdFrictionCurve.addPoint(1.00f, 0.65f, 0, 0);
        }

        /// <summary>
        /// Seat the reference to the wheel-impact callback.  This method will be called when the wheel first contacts the surface, passing in the wheel-local impact velocity
        /// </summary>
        /// <param name="callback"></param>
        public void setImpactCallback(Action<Vector3> callback)
        {
            onImpactCallback = callback;
        }

        /// <summary>
        /// Set the raycast layer mask
        /// </summary>
        /// <param name="mask"></param>
        public void setRaycastMask(int mask)
        {
            raycastMask = mask;
        }

        /// <summary>
        /// Set the current input state
        /// </summary>
        /// <param name="fwd"></param>
        /// <param name="rot"></param>
        public void setInputState(float fwd, float rot)
        {
            fwdInput = fwd;
            rotInput = rot;
        }

        /// <summary>
        /// Set the current steering angle explicitly, and update the internal rotInput state to match the new explicit steering angle
        /// </summary>
        /// <param name="angle"></param>
        public void setSteeringAngle(float angle)
        {
            currentSteerAngle = angle;
            rotInput = currentSteerAngle / maxSteerAngle;
        }

        /// <summary>
        /// UpdateWheel() should be called by the controlling component/container on every FixedUpdate that this wheel should apply forces for.<para/>
        /// Collider and physics integration can be disabled by simply no longer calling UpdateWheel
        /// </summary>
        public void UpdateWheel()
        {
            calculateSteeringAngle();
            wheelRPM = (wWheel * Mathf.Rad2Deg) / 60;
            iWheel = wheelMass * wheelRadius * wheelRadius * 0.5f;

            float rayDistance = suspensionLength + wheelRadius;

            wheelForward = Quaternion.AngleAxis(currentSteerAngle, wheel.transform.up) * wheel.transform.forward;
            wheelUp = wheel.transform.up;
            wheelRight = -Vector3.Cross(wheelForward, wheelUp);
            grounded = false;
            if (Physics.Raycast(wheel.transform.position, -wheel.transform.up, out hit, rayDistance, raycastMask))
            {
                prevCompressionDistance = compressionDistance;
                wheelMeshPosition = hit.point + (wheel.transform.up * wheelRadius);
                worldVelocityAtHit = rigidBody.GetPointVelocity(hit.point);
                wheelLocalVelocity.z = Vector3.Dot(worldVelocityAtHit.normalized, wheelForward) * worldVelocityAtHit.magnitude;
                wheelLocalVelocity.x = Vector3.Dot(worldVelocityAtHit.normalized, wheelRight) * worldVelocityAtHit.magnitude;
                wheelLocalVelocity.y = Vector3.Dot(worldVelocityAtHit.normalized, wheel.transform.up) * worldVelocityAtHit.magnitude;
                wheelMountLocalVelocity = wheel.transform.InverseTransformDirection(worldVelocityAtHit);//used for spring/damper 'velocity' value

                compressionDistance = suspensionLength + wheelRadius - (hit.distance);
                compressionPercent = compressionDistance / suspensionLength;
                compressionPercentInverse = 1.0f - compressionPercent;

                springVelocity = compressionDistance - prevCompressionDistance;
                dampForce = damper * springVelocity;

                springForce = (compressionDistance - (suspensionLength * target)) * spring;
                springForce += dampForce;
                if (springForce < 0) { springForce = 0; }

                if (Math.Abs(wheelLocalVelocity.x) < maxStickyVelocity) { sideStickyTimer++; }
                else { sideStickyTimer = 0; }
                if (Math.Abs(wheelLocalVelocity.z) < maxStickyVelocity) { fwdStickyTimer++; }
                else { fwdStickyTimer = 0; }
                //updateStickyJoint(springForce, fwdStickyTimer, sideStickyTimer);

                forceToApply = hit.normal * springForce;
                calcFriction(springForce);
                //forceToApply += fLong * wheelForward;
                //forceToApply += fLat * wheelRight;
                rigidBody.AddForceAtPosition(forceToApply, wheel.transform.position, ForceMode.Force);
                if (!grounded && onImpactCallback!=null)//if was not previously grounded, call-back with grounded state
                {
                    onImpactCallback.Invoke(wheelLocalVelocity);
                }
                grounded = true;
            }
            else
            {
                grounded = false;
                //springForce = dampForce = 0;
                //prevCompressionDistance = 0;
                //compressionDistance = 0;
                //compressionPercent = 0;
                //compressionPercentInverse = 1;
                //worldVelocityAtHit = Vector3.zero;
                //wheelMountLocalVelocity = Vector3.zero;
                //wheelLocalVelocity = Vector3.zero;
                wheelMeshPosition = wheel.transform.position + (-wheel.transform.up * suspensionLength * (1f - target));
                Component.Destroy(stickyJoint);
            }
        }

        #endregion ENDREGION - Public accessible methods / API methods

        #region REGION - Private/internal update methods

        /// <summary>
        /// Per-fixed-update configuration of the rigidbody joints that are used for sticky friction and anti-punchthrough behaviour
        /// //TODO -- anti-punchthrough setup; somehow ensure the part cannot actually punch-through by using joint constraints
        /// //TODO -- how to tell if it was a punch-through or the surface was moved? Perhaps start the raycast slightly above the wheel?
        /// //TODO -- or, start at center of wheel and validate that it cannot compress suspension past (travel-radius), 
        /// //TODO -- so there will be at least (radius*1) between the wheel origin and the surface
        /// </summary>
        /// <param name="fwd"></param>
        /// <param name="side"></param>
        private void updateStickyJoint(float downForce, int fwd, int side)
        {
            if (stickyJoint == null)
            {
                stickyJoint = rigidBody.gameObject.AddComponent<ConfigurableJoint>();
                stickyJoint.anchor = wheel.transform.localPosition;
                stickyJoint.axis = Vector3.right;
                stickyJoint.secondaryAxis = Vector3.up;
            }
            stickyJoint.connectedAnchor = hit.point;

            stickyJoint.breakForce = downForce;
            stickyJoint.breakTorque = downForce;

            //below were tests of using the sticky joint for suspension as well; sadly I was unable to figure out usable settings for it (it liked to bounce and oscillate)
            //stickyJoint.targetPosition = Vector3.up * (suspensionLength - target);
            //SoftJointLimitSpring sjls = new SoftJointLimitSpring();
            //sjls.spring = spring*0.5f;
            //sjls.damper = damper;
            //stickyJoint.linearLimitSpring = sjls;

            //JointDrive jd = new JointDrive();
            //jd.mode = JointDriveMode.Position;
            //jd.positionSpring = spring*0.5f;
            //jd.positionDamper = damper;
            //jd.maximumForce = float.PositiveInfinity;
            //stickyJoint.yDrive = jd;

            if (fwd > 5 && fwdInput==0)
            {
                stickyJoint.zMotion = ConfigurableJointMotion.Locked;
            }
            else
            {
                stickyJoint.zMotion = ConfigurableJointMotion.Free;
            }
            if (side > 5 && Mathf.Abs(wheelLocalVelocity.x) < maxStickyVelocity)
            {
                stickyJoint.xMotion = ConfigurableJointMotion.Locked;
            }
            else
            {
                stickyJoint.xMotion = ConfigurableJointMotion.Free;
            }
        }

        //TODO use proper friction curve, wheel RPM, wheel-mass, and downforce to determine fwd friction
        private float calculateForwardFriction(float downForce)
        {
            calculateWheelSlip();
            calcLongFriction(downForce);
            
            //update visual wheel RPM from current angular velocity
            //convert from radians per second into rotations per minute
            return 0;
        }
        
        private float calculateSideFriction(float downForce)
        {
            float absSlipVel = Mathf.Abs(wheelLocalVelocity.x);
            float normSlipVal = Mathf.Abs(wheelLocalVelocity.normalized.x);
            float force = sideFrictionCurve.evaluate(normSlipVal) * downForce;
            if (force > absSlipVel * downForce * 2f ) { force = absSlipVel * downForce * 2f; }
            float sideSlip = -Mathf.Sign(wheelLocalVelocity.x) * force;
            return sideSlip;
        }
        
        /// <summary>
        /// Updates the current steering angle from the current rotation input (-1...0...1), maxSteerAngle (config param), and steerLerpSpeed (response speed)
        /// </summary>
        private void calculateSteeringAngle()
        {
            currentSteerAngle = Mathf.Lerp(currentSteerAngle, rotInput * maxSteerAngle, Time.fixedDeltaTime * steerLerpSpeed);
        }
        
        #region REGION - Friction calculation methods based on Physx origin: http://www.eggert.highpeakpress.com/ME485/Docs/CarSimEd.pdf
        
        /// <summary>
        /// Calculate the wheel lat and long slips from the current axis velocities and wheel rotational speed
        /// </summary>
        private void calculateWheelSlip()
        {
            float maxDenom = 0.01f;
            float vZ = wheelLocalVelocity.z;//m/s
            float vX = wheelLocalVelocity.x;//m/s
            float vW = wWheel * wheelRadius;//m/s @ radius

            float absVZ = Mathf.Abs(vZ);
            float absVW = Mathf.Abs(vW);

            sLat = Mathf.Atan(vX / (vZ+0.1f));//??
            if (vW == 0 && vZ == 0)
            {
                sLong = 0f;                
            }
            //else if (brakeInput != 0 || fwdInput != 0)//user-input to change velocity
            //{
            //    longSlip = (vW - vZ) / (Mathf.Max(absVZ, absVW) + 0.1f * fudge1);
            //}
            else
            {
                sLong = (vW - vZ) / Mathf.Max(maxDenom, Mathf.Max(absVZ, absVW));
            }
        }

        private void calcLongFriction(float downForce)
        {
            float longForce = fwdFrictionCurve.evaluate(sLong) * downForce;
        }

        /// <summary>
        /// http://www.eggert.highpeakpress.com/ME485/Docs/CarSimEd.pdf equation 20
        /// </summary>
        /// <param name="k"></param>
        /// <returns></returns>
        private float smooth1(float k)
        {
            float oneThird = 0.33333333333f;
            float oneTwentySeventh = 0.03703703703f;
            return Mathf.Min(1, k - oneThird * k * k + oneTwentySeventh * k * k * k);
        }

        /// <summary>
        /// http://www.eggert.highpeakpress.com/ME485/Docs/CarSimEd.pdf equation 21
        /// </summary>
        /// <param name="k"></param>
        /// <returns></returns>
        private float smooth2(float k)
        {
            float oneThird = 0.33333333333f;
            float oneTwentySeventh = 0.03703703703f;
            return (k - (k * k) + (oneThird * k * k * k) - (oneTwentySeventh * k * k * k * k));
        }

        #endregion ENDREGION - Friction Calculation Methods

        #region REGION - Friction calculations methods based on alternate source: http://www.asawicki.info/Mirror/Car%20Physics%20for%20Games/Car%20Physics%20for%20Games.html

        /// <summary>
        /// Working, but incomplete; fwd traction input/output needs attention
        /// </summary>
        /// <param name="downForce"></param>
        private void calcFriction(float downForce)
        {
            //TODO add rolling resistance calculation
            float constRR = 13;// rolling resistance drag constant

            //integrate drive torque before calculating slips; so that end wheel rpm = after friction applied... a visual thing...
            tDrive = fwdInput * motorTorque;
            wWheel += tDrive / iWheel;
            //should also apply brake torques prior to traction?

            vLong = wheelLocalVelocity.z;
            vLat = wheelLocalVelocity.x;            
            vWheel = wWheel * wheelRadius;
            sLong = (vWheel - vLong) / Mathf.Abs(vLong);
            //sLong = (Mathf.Max(vLong, vWheel) - Mathf.Min(vLong, vWheel)) / Mathf.Max(Mathf.Abs(vLong), Mathf.Abs(vWheel));
            if (vLong == 0 && vWheel==0)//for cases where wheel is spinning but not moving, solves div/0 errors for vLongAbs as the denom
            {
                sLong = 0;
            }
            //raw longitudinal force based purely on the slip ratio
            fLong = fwdFrictionCurve.evaluate(Mathf.Abs(sLong)) * downForce * -Mathf.Sign(vLong);

            float latSlipRadians = Mathf.Abs(Mathf.Atan(vLat / vLong));
            if (vLat == 0) { latSlipRadians = 0f; }//zero slip angle if no lateral movement
            else if (vLong == 0) { latSlipRadians = 0.5f * Mathf.PI; }//90' angle if there is no long movement
            float latSlipAngle = Mathf.Rad2Deg * latSlipRadians;
            sLat = latSlipAngle / 90f;//convert from an angle (0-90) into a percent (0-1) for use with the friction curve system
            //raw lateral force based purely on the slip ratio
            fLat = sideFrictionCurve.evaluate(sLat) * downForce;
            if (fLat > Mathf.Abs(vLat) * downForce * 2f) { fLat = Mathf.Abs(vLat) * downForce * 2f; }
            fLat *= -Mathf.Sign(vLat);

            float vDelta = (vWheel - vLong);
            float wDelta = vDelta / wheelRadius;
            float tDelta = wDelta * iWheel;
            float fDelta = tDelta / wheelRadius;
            tBrake = brakeInput * this.brakeTorque;
            tRoll = 0;
            float tTractMax = Mathf.Abs(wDelta * iWheel);
            tTract = Mathf.Min(tTractMax, Mathf.Abs(fLong) / iWheel) * -Mathf.Sign(sLong);
            fLong = -(fDelta / wheelRadius);
            tTotal = tBrake + tRoll + tTract;
            wAccel = tTotal / iWheel;

            wWheel += wAccel;
        }

        #endregion ENDREGION - Friction calculations methods based on alternate source: 
        
        #endregion ENDREGION - Private/internal update methods

    }
}