﻿using UnityEngine;

namespace KSPWheel
{
    /// <summary>
    /// This class is a wrapper around the KSPWheelCollider class to allow for easier use while in the Unity Editor.<para/>
    /// It will merely instantiate a KSPWheelCollider object and update its internal variables with the ones entered into the Editor Inspector panel.<para/>
    /// Also includes a few display-only variables for debugging in the editor
    /// </summary>
    [AddComponentMenu("Physics/KSPWheel")]
    public class KSPWheelComponent : MonoBehaviour
    {

        #region REGION - Unity Editor Inspector Assignable Fields
        // These variables are set onto the KSPWheelCollider object when Start is called,
        // and updated during script OnValidate() to update any changed values from the editor inspector panel

        /// <summary>
        /// The rigidbody that this wheel will apply forces to and sample velocity from
        /// </summary>
        public Rigidbody rigidBody;

        public Transform steeringTransform;

        public Transform suspensionTransform;

        public Transform wheelTransform;

        /// <summary>
        /// The radius of the wheel to simulate; this is the -actual- size to simulate, not a pre-scaled value
        /// </summary>
        public float wheelRadius = 0.5f;

        /// <summary>
        /// The mass of the -wheel- in... kg? tons? NFC
        /// </summary>
        public float wheelMass = 1f;//used to simulate wheel rotational inertia for brakes and friction purposes

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
        public float spring = 1000;

        /// <summary>
        /// The damping ratio for the suspension spring force
        /// </summary>
        public float damper = 1500;

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
        /// The forward friction constant (rolling friction)
        /// </summary>
        public float fwdFrictionConst = 0.001f;

        /// <summary>
        /// The sideways friction constant
        /// </summary>
        public float sideFrictionConst = 1f;

        /// <summary>
        /// If should use differential motor input for steering
        /// </summary>
        public bool tankSteer = false;

        /// <summary>
        /// If this wheel should have its steering inverted; to be used on 'right side' wheels
        /// </summary>
        public bool invertSteer = false;

        public bool sphereCast = false;

        /// <summary>
        /// If true, display debug gizmos in the editor
        /// TODO add some sort of debug drawing for play mode (line-renderer?)
        /// </summary>
        public bool debug = false;       

        #endregion ENDREGION - Unity Editor Inspector Assignable Fields

        #region REGION - Unity Editor Display Variables
        //these variables are updated every fixed-tick after the wheel has been updated
        //used merely to display some info while in the editor

        public float springForce;
        public float dampForce;
        public float currentSteerAngle;
        public Vector3 worldVelocity;
        public Vector3 localVelocity;
        public Vector3 totalLocalForce;
        public Vector3 hitNormal;

        public float wheelRPM;

        public float sLong;
        public float sLat;

        public float fLong;
        public float fLat;

        #endregion ENDREGION - Unity Editor Display Variables

        private KSPWheelCollider wheelCollider;
        private float fwdInput;
        private float rotInput;
        private float brakeInput;

        private float throttleResponse = 2;
        private float steeringResponse = 2;
        private float brakeResponse = 2;

        private float curThrottle;
        private float curSteer;
        private float curBrake;

        public void Start()
        {
            wheelCollider = new KSPWheelCollider(gameObject, rigidBody);
            OnValidate();
        }

        private void sampleInput()
        {
            float left = Input.GetKey(KeyCode.A) ? -1 : 0;
            float right = Input.GetKey(KeyCode.D) ? 1 : 0;
            float fwd = Input.GetKey(KeyCode.W) ? 1 : 0;
            float rev = Input.GetKey(KeyCode.S) ? -1 : 0;
            brakeInput = Input.GetKey(KeyCode.Space) ? 1 : 0;
            fwdInput = fwd + rev;
            rotInput = left + right;
            if (invertSteer) { rotInput = -rotInput; }
            if (tankSteer)
            {
                fwdInput = fwdInput + rotInput;
                if (fwdInput > 1) { fwdInput = 1; }
                if (fwdInput < -1) { fwdInput = -1; }
            }
            curThrottle = Mathf.Lerp(curThrottle, fwdInput * motorTorque, throttleResponse);
            curSteer = Mathf.Lerp(curSteer, rotInput * maxSteerAngle, steeringResponse);
            curBrake = Mathf.Lerp(curBrake, brakeInput * brakeTorque, brakeResponse);
        }

        public void FixedUpdate()
        {
            sampleInput();
            wheelCollider.motorTorque = curThrottle;
            wheelCollider.steeringAngle = curSteer;
            wheelCollider.brakeTorque = curBrake;
            wheelCollider.updateWheel();
            if (steeringTransform != null)
            {
                steeringTransform.localRotation = Quaternion.AngleAxis(currentSteerAngle, steeringTransform.up);
            }
            if (suspensionTransform != null)
            {
                suspensionTransform.position = gameObject.transform.position - (suspensionLength - wheelCollider.compressionDistance) * gameObject.transform.up;
            }
            if (wheelTransform != null)
            {
                wheelTransform.Rotate(wheelTransform.right, wheelCollider.perFrameRotation, Space.World);
            }
            totalLocalForce = wheelCollider.calculatedForces;
            hitNormal = wheelCollider.hit.normal;
            worldVelocity = wheelCollider.worldVelocityAtHit;
            localVelocity = wheelCollider.wheelLocalVelocity;
            springForce = wheelCollider.springForce;
            dampForce = wheelCollider.dampForce;
            wheelRPM = wheelCollider.rpm;
            sLong = wheelCollider.longitudinalSlip;
            sLat = wheelCollider.lateralSlip;
            fLong = wheelCollider.longitudinalForce;
            fLat = wheelCollider.lateralForce;
        }

        public void OnValidate()
        {
            if (wheelCollider != null)
            {
                wheelCollider.radius = wheelRadius;
                wheelCollider.mass = wheelMass;
                wheelCollider.length = suspensionLength;
                wheelCollider.target = target;
                wheelCollider.spring = spring;
                wheelCollider.damper = damper;
                wheelCollider.motorTorque = motorTorque;
                wheelCollider.brakeTorque = brakeTorque;
                wheelCollider.forwardFrictionCoefficient = fwdFrictionConst;
                wheelCollider.sideFrictionCoefficient = sideFrictionConst;
                wheelCollider.sphereCast = sphereCast;
            }
        }

        //private void drawDebug()
        //{
        //    Vector3 rayStart = gameObject.transform.position;
        //    Vector3 rayEnd = rayStart - gameObject.transform.up * (suspensionLength + wheelRadius);
        //    Vector3 velocity = rigidBody.velocity * Time.deltaTime;

        //    Debug.DrawLine(rayStart + velocity, rayEnd + velocity, Color.green);//Y-axis of WC

        //    Debug.DrawLine(gameObject.transform.position - gameObject.transform.right * 0.25f + velocity, gameObject.transform.position + gameObject.transform.right * 0.25f + velocity, Color.red);//X-axis of wheel collider transform
        //    Debug.DrawLine(gameObject.transform.position - gameObject.transform.forward * 0.25f + velocity, gameObject.transform.position + gameObject.transform.forward * 0.25f + velocity, Color.blue);//Z-axis of wheel collider transform

        //    Vector3 lineStart = gameObject.transform.position + (-gameObject.transform.up * suspensionLength * (1f - target));
        //    Debug.DrawLine(lineStart - gameObject.transform.right * 0.25f + velocity, lineStart + gameObject.transform.right * 0.25f + velocity, Color.red);//X-axis of wheel collider transform
        //    Debug.DrawLine(lineStart - gameObject.transform.forward * 0.25f + velocity, lineStart + gameObject.transform.forward * 0.25f + velocity, Color.blue);//Z-axis of wheel collider transform

        //    if (wheelCollider.grounded)
        //    {
        //        rayStart = wheelCollider.wheel.transform.position + velocity;
        //        rayEnd = rayStart + (wheelCollider.wheelUp * 10);
        //        Debug.DrawLine(rayStart, rayEnd, Color.magenta);

        //        rayEnd = wheelCollider.hit.point + velocity + (wheelCollider.wheelForward * 10);
        //        Debug.DrawLine(rayStart, rayEnd, Color.magenta);

        //        rayEnd = wheelCollider.hit.point + velocity + (wheelCollider.wheelRight * 10);
        //        Debug.DrawLine(rayStart, rayEnd, Color.magenta);

        //        rayEnd = wheelCollider.hit.point + velocity + (wheelCollider.forceToApply);
        //        Debug.DrawLine(rayStart, rayEnd, Color.gray);

        //        rayStart = rigidBody.position + velocity;
        //        rayEnd = rayStart + rigidBody.velocity.normalized * 10f;
        //        Debug.DrawLine(rayStart, rayEnd, Color.blue);
        //    }

        //    drawDebugWheel();
        //}

        //private void drawDebugWheel()
        //{
        //    //Draw the wheel
        //    Vector3 velocity = rigidBody.velocity * Time.deltaTime;
        //    Vector3 diff = -gameObject.transform.up * (suspensionLength - wheelCollider.compressionDistance) + velocity;
        //    float radius = wheelRadius;
        //    Vector3 point1;
        //    Vector3 point0 = gameObject.transform.TransformPoint(radius * new Vector3(0, Mathf.Sin(0), Mathf.Cos(0))) + diff;
        //    for (int i = 1; i <= 20; ++i)
        //    {
        //        point1 = gameObject.transform.TransformPoint(radius * new Vector3(0, Mathf.Sin(i / 20.0f * Mathf.PI * 2.0f), Mathf.Cos(i / 20.0f * Mathf.PI * 2.0f))) + diff;
        //        Debug.DrawLine(point0, point1, Color.red);
        //        point0 = point1;
        //    }
        //}


        /// <summary>
        /// Display a visual representation of the wheel in the editor. Unity has no inbuilt gizmo for 
        /// circles, so a sphere is used. Unlike the original WC, I've represented the wheel at top and bottom 
        /// of suspension travel
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (debug)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(gameObject.transform.position, wheelRadius);
                Vector3 pos2 = gameObject.transform.position + -gameObject.transform.up * suspensionLength;
                if (wheelCollider != null) { pos2 += gameObject.transform.up * wheelCollider.compressionDistance; }
                Gizmos.DrawWireSphere(pos2, wheelRadius);
                Gizmos.DrawRay(gameObject.transform.position - gameObject.transform.up * wheelRadius, -gameObject.transform.up * suspensionLength);
            }
        }

    }
}
