using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	/// Use this component for small objects that need physical simulation.
	/// </summary>
	public class FastWaterPhysics : MonoBehaviour
	{
		[SerializeField]
		private Water water;

		[Tooltip("Adjust buoyancy proportionally, if your collider is bigger or smaller than the actual object. Lowering this may fix some weird behaviour of objects with extremely low density like beach balls or baloons.")]
		[SerializeField]
		private float buoyancyIntensity = 1.0f;

		[Range(0.0f, 3.0f)]
		[Tooltip("Controls drag force. Determined experimentally in wind tunnels. Example values:\n https://en.wikipedia.org/wiki/Drag_coefficient#General")]
		[SerializeField]
		private float dragCoefficient = 0.9f;

		[Tooltip("Horizontal flow force intensity.")]
		[SerializeField]
		private float flowIntensity = 1.0f;

		private Rigidbody rigidBody;
		private Collider localCollider;
		private WaterSample sample;
		private float lastPositionX, lastPositionZ;
		private Vector3 buoyancyPart;
		private float dragPart, flowPart;
		private float volume, area;
		private bool useCheapDrag, useCheapFlow;

		private static Ray rayUp;
		private static Ray rayDown;

		private void Awake()
		{
			rayUp = new Ray(Vector3.zero, Vector3.up);
			rayDown = new Ray(Vector3.zero, Vector3.down);
		}

		private void Start()
		{
			rigidBody = GetComponentInParent<Rigidbody>();
			localCollider = GetComponentInParent<Collider>();

			if(water == null)
				water = WaterGlobals.Instance.BoundlessWaters[0];

			OnValidate();

			Vector3 position = transform.position;
			lastPositionX = position.x;
			lastPositionZ = position.z;

			sample = new WaterSample(water, WaterSample.DisplacementMode.HeightAndForces);
			sample.Start(transform.position);
		}

		private void OnValidate()
		{
			if(localCollider != null)
			{
				volume = localCollider.ComputeVolume();
				area = localCollider.ComputeArea();
			}

			if(flowIntensity < 0) flowIntensity = 0;
			if(buoyancyIntensity < 0) buoyancyIntensity = 0;

			if(water != null)
			{
				PrecomputeBuoyancy();
				PrecomputeDrag();
				PrecomputeFlow();
			}
		}

		private void FixedUpdate()
		{
			if (rigidBody.isKinematic)
				return;

			var bounds = localCollider.bounds;
			float min = bounds.min.y;
			float max = bounds.max.y;

			Vector3 velocity, sqrVelocity, dragForce, force;
			Vector3 displaced = new Vector3();
			Vector3 flowForce = new Vector3();
			Vector3 position = transform.position;
			float height = max - min + 80.0f;
			float fixedDeltaTime = Time.fixedDeltaTime;
			float forceToVelocity = fixedDeltaTime*(1.0f - rigidBody.drag*fixedDeltaTime)/rigidBody.mass;
			float time = water.Time;
			RaycastHit hitInfo;

			/*
			 * Compute new samples.
			 */
			Vector3 point = transform.position;
			sample.GetAndResetFast(point.x, point.z, time, ref displaced, ref flowForce);

			displaced.x += position.x - lastPositionX;
			displaced.z += position.z - lastPositionZ;

			float waterHeight = displaced.y;
			displaced.y = min - 20.0f;
			rayUp.origin = displaced;

			if (localCollider.Raycast(rayUp, out hitInfo, height))
			{
				float low = hitInfo.point.y;
				Vector3 normal = hitInfo.normal;

				displaced.y = max + 20.0f;
				rayDown.origin = displaced;
				localCollider.Raycast(rayDown, out hitInfo, height);

				float high = hitInfo.point.y;
				float frc = (waterHeight - low)/(high - low);

				if (!(frc > 0.0f)) // this condition looks weird, but includes NaNs
					return;

				if (frc > 1.0f)
					frc = 1.0f;

				// buoyancy
				force.x = buoyancyPart.x*frc;
				force.y = buoyancyPart.y*frc;
				force.z = buoyancyPart.z*frc;

				float t = frc*0.5f;
				displaced.y = low*(1.0f - t) + high*t;

				// hydrodynamic drag
				if (useCheapDrag)
				{
					Vector3 pointVelocity = rigidBody.GetPointVelocity(displaced);
					velocity.x = pointVelocity.x + force.x*forceToVelocity;
					velocity.y = pointVelocity.y + force.y*forceToVelocity;
					velocity.z = pointVelocity.z + force.z*forceToVelocity;

					sqrVelocity.x = velocity.x > 0.0f ? -velocity.x*velocity.x : velocity.x*velocity.x;
					sqrVelocity.y = velocity.y > 0.0f ? -velocity.y*velocity.y : velocity.y*velocity.y;
					sqrVelocity.z = velocity.z > 0.0f ? -velocity.z*velocity.z : velocity.z*velocity.z;

					dragForce.x = dragPart*sqrVelocity.x;
					dragForce.y = dragPart*sqrVelocity.y;
					dragForce.z = dragPart*sqrVelocity.z;

					float dragVelocityDelta = dragForce.magnitude*forceToVelocity;
					float dragVelocityDeltaSq = dragVelocityDelta*dragVelocityDelta;
					float pointVelocitySq = pointVelocity.x*pointVelocity.x + pointVelocity.y*pointVelocity.y + pointVelocity.z*pointVelocity.z;

					// limit drag to avoid inverting velocity direction
					if (dragVelocityDeltaSq > pointVelocitySq)
						frc *= Mathf.Sqrt(pointVelocitySq)/dragVelocityDelta;

					force.x += dragForce.x*frc;
					force.y += dragForce.y*frc;
					force.z += dragForce.z*frc;
				}

				// apply buoyancy and drag
				rigidBody.AddForceAtPosition(force, displaced, ForceMode.Force);

				if (useCheapFlow)
				{
					// flow force
					float flowForceMagnitude = flowForce.x*flowForce.x + flowForce.y*flowForce.y + flowForce.z*flowForce.z;

					if (flowForceMagnitude != 0)
					{
						t = -1.0f/flowForceMagnitude;
						float d = (normal.x*flowForce.x + normal.y*flowForce.y + normal.z*flowForce.z)*t + 0.5f;

						if (d > 0)
						{
							// apply flow force
							force = flowForce*(d*flowPart);
							displaced.y = low;
							rigidBody.AddForceAtPosition(force, displaced, ForceMode.Force);
						}
					}
				}

	#if UNITY_EDITOR
				if (WaterProjectSettings.Instance.DebugPhysics)
				{
					displaced.y = waterHeight;
					Debug.DrawLine(displaced, displaced + force/rigidBody.mass, Color.white, 0.0f, false);
				}
	#endif
			}

			lastPositionX = position.x;
			lastPositionZ = position.z;
		}

		private void PrecomputeBuoyancy()
		{
			buoyancyPart = -Physics.gravity * (volume * buoyancyIntensity * water.Density);
		}

		private void PrecomputeDrag()
		{
			useCheapDrag = dragCoefficient > 0.0f;
			dragPart = 0.25f * dragCoefficient * area * water.Density;
		}

		private void PrecomputeFlow()
		{
			useCheapFlow = flowIntensity > 0.0f;
			flowPart = flowIntensity * dragCoefficient * area * 100.0f;
		}
	}
}