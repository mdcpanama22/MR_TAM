using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;

namespace PlayWay.Water
{
	/// <summary>
	///     Component that applies buoyancy, flow and drag forces to the rigid body.
	/// </summary>
	[RequireComponent(typeof(Rigidbody))]
	public sealed class WaterPhysics : MonoBehaviour
	{
		[Tooltip("Controls precision of the simulation. Keep it low (1 - 2) for small and not important objects. Prefer high values (15 - 30) for ships etc.")]
		[Range(1, 30)]
		[SerializeField]
		private int sampleCount = 20;

		[Range(0.0f, 3.0f)]
		[Tooltip("Controls drag force. Determined experimentally in wind tunnels. Example values:\n https://en.wikipedia.org/wiki/Drag_coefficient#General")]
		[SerializeField]
		private float dragCoefficient = 0.9f;
		
		[Range(0.125f, 1.0f)]
		[Tooltip("Determines how many waves will be used in computations. Set it low for big objects, larger than most of the waves. Set it high for smaller objects of size comparable to many waves.")]
		[SerializeField]
		private float precision = 0.5f;

		[Tooltip("Adjust buoyancy proportionally, if your collider is bigger or smaller than the actual object. Lowering this may fix some weird behaviour of objects with extremely low density like beach balls or baloons.")]
		[SerializeField]
		private float buoyancyIntensity = 1.0f;

		[Tooltip("Horizontal flow force intensity.")]
		[SerializeField]
		private float flowIntensity = 1.0f;

		[Tooltip("Temporarily supports only mesh colliders.")]
		[SerializeField]
		private bool useImprovedDragAndFlowForces;

		private Vector3[] cachedSamplePositions;
		private int cachedSampleIndex;
		private int cachedSampleCount;

		private Collider localCollider;
		private Rigidbody rigidBody;

		private float volume;
		private float area = -1.0f;
		private float totalArea;

		private WaterSample[] samples;

		// precomputed stuff
		private float numSamplesInv;
		private Vector3 buoyancyPart;
		private Vector3 improvedBuoyancyPart;
		private float dragPart;
		private float improvedDragPart;
		private float flowPart;
		private float improvedFlowPart;
		private float averageWaterElevation;
		private bool useCheapDrag, useCheapFlow;
		private Water waterOverride;
		private WaterVolumeProbe waterProbe;
		private float lastPositionX, lastPositionZ;
		private Vector3[] dragNormals;
		private Vector3[] dragCenters;
		private Vector3[] dragVertices;
		private float[] polygonVolumes;
		private float[] dragAreas;
		private WaterSample[] improvedDragSamples;

		private static Ray rayUp;
		private static Ray rayDown;

		private void Awake()
		{
			localCollider = GetComponent<Collider>();
			rigidBody = GetComponentInParent<Rigidbody>();
			
			rayUp = new Ray(Vector3.zero, Vector3.up);
			rayDown = new Ray(Vector3.zero, Vector3.down);

			if(localCollider == null || rigidBody == null)
			{
				Debug.LogError("WaterPhysics component is attached to an object without any Collider and/or RigidBody.");
				enabled = false;
				return;
			}

			Vector3 position = transform.position;
			lastPositionX = position.x;
			lastPositionZ = position.z;

			OnValidate();
			PrecomputeSamples();

			if(useImprovedDragAndFlowForces)
				PrecomputeImprovedDrag();
		}

		/// <summary>
		/// Water in which this rigid body is currently submerged in.
		/// </summary>
		public Water AffectingWater
		{
			get { return (object)waterProbe != null ? waterProbe.CurrentWater : waterOverride; }
			set
			{
				bool wasNull = waterOverride == null;

				waterOverride = value;

				if(waterOverride == null)
				{
					if(!wasNull)
						OnWaterLeave();

					CreateWaterProbe();
				}
				else
				{
					DestroyWaterProbe();
					OnWaterLeave();
					OnWaterEnter();
				}
            }
		}

		/// <summary>
		/// Scale for buoyancy force intensity.
		/// 1.0 is the base value.
		/// </summary>
		public float BuoyancyIntensity
		{
			get { return buoyancyIntensity; }
			set
			{
				buoyancyIntensity = value;

				if(AffectingWater != null)
					PrecomputeBuoyancy();
			}
		}

		/// <summary>
		/// Controls drag force. Determined experimentally in wind tunnels. Example values:
		/// https://en.wikipedia.org/wiki/Drag_coefficient#General
		/// </summary>
		public float DragCoefficient
		{
			get { return dragCoefficient; }
			set
			{
				dragCoefficient = value;

				if(AffectingWater != null)
					PrecomputeDrag();
			}
		}

		/// <summary>
		/// Scale for flow force intensity. It is the force applied directly by the collision with the waves.
		/// 1.0 is the base value.
		/// </summary>
		public float FlowIntensity
		{
			get { return flowIntensity; }
			set
			{
				flowIntensity = value;

				if(AffectingWater != null)
					PrecomputeFlow();
			}
		}

		public float AverageWaterElevation
		{
			get { return averageWaterElevation; }
		}

		/// <summary>
		/// Computes and returns total buoyancy force applied when the object is completely submerged.
		/// </summary>
		/// <param name="fluidDensity"></param>
		/// <returns></returns>
		public float GetTotalBuoyancy(float fluidDensity = 999.8f)
		{
#if UNITY_EDITOR
			if(!Application.isPlaying && !ValidateForEditor())
				return 0.0f;
#endif

			return Physics.gravity.magnitude * volume * buoyancyIntensity * fluidDensity / rigidBody.mass;
		}

		private void OnEnable()
		{
			if(waterOverride == null)
				CreateWaterProbe();
        }

		private void OnDisable()
		{
			DestroyWaterProbe();
			OnWaterLeave();
		}

		private void OnValidate()
		{
			numSamplesInv = 1.0f / sampleCount;
			
			if(localCollider != null)
			{
				volume = localCollider.ComputeVolume();
				area = localCollider.ComputeArea();
				
				if (totalArea == 0.0f)
					UpdateTotalArea();

				if(useImprovedDragAndFlowForces && !(localCollider is MeshCollider))
				{
					useImprovedDragAndFlowForces = false;

					Debug.LogErrorFormat("Improved drag force won't work colliders other than mesh colliders. '{0}' collider has a wrong type.", name);
				}

				if(useImprovedDragAndFlowForces && ((MeshCollider)localCollider).sharedMesh.vertexCount > 3000)
				{
					useImprovedDragAndFlowForces = false;

					var mesh = ((MeshCollider)localCollider).sharedMesh;
					Debug.LogErrorFormat("Improved drag force won't work with meshes that have more than 3000 vertices. '{0}' has {1} vertices.", mesh.name, mesh.vertexCount);
				}
			}

			if(flowIntensity < 0) flowIntensity = 0;
			if(buoyancyIntensity < 0) buoyancyIntensity = 0;

			if(AffectingWater != null)
			{
				PrecomputeBuoyancy();
				PrecomputeDrag();
				PrecomputeFlow();
			}
		}

		private void FixedUpdate()
		{
			if (useImprovedDragAndFlowForces)
				ImprovedFixedUpdate();
			else
				SimpleFixedUpdate();
		}

		private void SimpleFixedUpdate()
		{
			var currentWater = AffectingWater;
			
			if((object)currentWater == null || rigidBody.isKinematic)
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
			float forceToVelocity = fixedDeltaTime * (1.0f - rigidBody.drag * fixedDeltaTime) / rigidBody.mass;
			float time = currentWater.Time;
			averageWaterElevation = 0.0f;
			RaycastHit hitInfo;

			/*
			 * Compute new samples.
			 */
			for(int i = 0; i < sampleCount; ++i)
			{
				Vector3 point = transform.TransformPoint(cachedSamplePositions[cachedSampleIndex]);
				samples[i].GetAndResetFast(point.x, point.z, time, ref displaced, ref flowForce);

				displaced.x += position.x - lastPositionX;
				displaced.z += position.z - lastPositionZ;

				float waterHeight = displaced.y;
				displaced.y = min - 20.0f;
				rayUp.origin = displaced;

				averageWaterElevation += waterHeight;

				if(localCollider.Raycast(rayUp, out hitInfo, height))
				{
					float low = hitInfo.point.y;
					Vector3 normal = hitInfo.normal;

					displaced.y = max + 20.0f;
					rayDown.origin = displaced;
					localCollider.Raycast(rayDown, out hitInfo, height);

					float high = hitInfo.point.y;
					float frc = (waterHeight - low) / (high - low);

					if(!(frc > 0.0f))           // this condition looks weird, but includes NaNs
						continue;

					if(frc > 1.0f)
						frc = 1.0f;

					// buoyancy
					force.x = buoyancyPart.x * frc;
					force.y = buoyancyPart.y * frc;
					force.z = buoyancyPart.z * frc;

					float t = frc * 0.5f;
					displaced.y = low * (1.0f - t) + high * t;

					// hydrodynamic drag
					if(useCheapDrag)
					{
						Vector3 pointVelocity = rigidBody.GetPointVelocity(displaced);
						velocity.x = pointVelocity.x + force.x * forceToVelocity;
						velocity.y = pointVelocity.y + force.y * forceToVelocity;
						velocity.z = pointVelocity.z + force.z * forceToVelocity;

						sqrVelocity.x = velocity.x > 0.0f ? -velocity.x * velocity.x : velocity.x * velocity.x;
						sqrVelocity.y = velocity.y > 0.0f ? -velocity.y * velocity.y : velocity.y * velocity.y;
						sqrVelocity.z = velocity.z > 0.0f ? -velocity.z * velocity.z : velocity.z * velocity.z;

						dragForce.x = dragPart * sqrVelocity.x;
						dragForce.y = dragPart * sqrVelocity.y;
						dragForce.z = dragPart * sqrVelocity.z;

						float dragVelocityDelta = dragForce.magnitude * forceToVelocity;
						float dragVelocityDeltaSq = dragVelocityDelta * dragVelocityDelta;
						float pointVelocitySq = pointVelocity.x * pointVelocity.x + pointVelocity.y * pointVelocity.y + pointVelocity.z * pointVelocity.z;

						// limit drag to avoid inverting velocity direction
						if(dragVelocityDeltaSq > pointVelocitySq)
							frc *= Mathf.Sqrt(pointVelocitySq) / dragVelocityDelta;

						force.x += dragForce.x * frc;
						force.y += dragForce.y * frc;
						force.z += dragForce.z * frc;
					}

					// apply buoyancy and drag
					rigidBody.AddForceAtPosition(force, displaced, ForceMode.Force);

					if(useCheapFlow)
					{
						// flow force
						float flowForceMagnitude = flowForce.x * flowForce.x + flowForce.y * flowForce.y + flowForce.z * flowForce.z;

						if(flowForceMagnitude != 0)
						{
							t = -1.0f / flowForceMagnitude;
							float d = (normal.x * flowForce.x + normal.y * flowForce.y + normal.z * flowForce.z) * t + 0.5f;

							if(d > 0)
							{
								// apply flow force
								force = flowForce * (d * flowPart);
								displaced.y = low;
								rigidBody.AddForceAtPosition(force, displaced, ForceMode.Force);
							}
						}
					}

#if UNITY_EDITOR
					if(WaterProjectSettings.Instance.DebugPhysics)
					{
						displaced.y = waterHeight;
						Debug.DrawLine(displaced, displaced + force / rigidBody.mass, Color.white, 0.0f, false);
					}
#endif
				}

				if(++cachedSampleIndex >= cachedSampleCount)
					cachedSampleIndex = 0;
			}

			averageWaterElevation *= numSamplesInv;

			lastPositionX = position.x;
			lastPositionZ = position.z;
		}

		private void ImprovedFixedUpdate()
		{
			var currentWater = AffectingWater;
			
			if((object)currentWater == null || rigidBody.isKinematic)
				return;
			
			Vector3 force;
			float waterElevation = 0.0f;
			Vector3 flowForce = new Vector3();
			float time = currentWater.Time;

			float improvedDragPart = this.improvedDragPart;
			Matrix4x4 localToWorldMatrix = transform.localToWorldMatrix;
			Vector4 localToWorldRow1 = localToWorldMatrix.GetRow(1);
			Vector3 center = localCollider.bounds.center;
			averageWaterElevation = 0.0f;
			int vertexIndex = 0;

			for (int i = 0; i < dragNormals.Length; ++i)
			{
				Vector3 polygonCenter = localToWorldMatrix.MultiplyPoint3x4(dragCenters[i]);
				Vector3 v = rigidBody.GetPointVelocity(polygonCenter);
				Vector3 wn = localToWorldMatrix.MultiplyVector(dragNormals[i]);

				improvedDragSamples[i].GetAndResetFast(polygonCenter.x, polygonCenter.z, time, ref waterElevation, ref flowForce);

				averageWaterElevation += waterElevation;

				float dotDrag = wn.x*v.x + wn.y*v.y + wn.z*v.z;
				float dotFlow = (flowForce.x*wn.x + flowForce.y*wn.y + flowForce.z*wn.z)*improvedFlowPart;

				float p;

				if (dotDrag > 0.0f || dotFlow > 0.0f)
				{
					float a = SingleComponentTransform(ref dragVertices[vertexIndex++], ref localToWorldRow1);
					float b = SingleComponentTransform(ref dragVertices[vertexIndex++], ref localToWorldRow1);
					float c = SingleComponentTransform(ref dragVertices[vertexIndex++], ref localToWorldRow1);

					float da = waterElevation - a;
					float db = waterElevation - b;
					float dc = waterElevation - c;

					if (da > 0.0f)
					{
						if (db > 0.0f)
							p = dc >= 0.0f ? 1.0f : (da + db)/(da + db - dc);
						else
							p = dc >= 0.0f ? (da + dc)/(da - db + dc) : da/(da - db - dc);
					}
					else
					{
						if (db > 0.0f)
							p = dc >= 0.0f ? (db + dc)/(db + dc - da) : db/(db - dc - da);
						else
							p = dc >= 0.0f ? dc/(dc - da - db) : 0.0f;
					}

					if (!(p > 0.0f && p <= 1.02f))
						p = 0.0f;
				}
				else
				{
					p = 0.0f;
					vertexIndex += 3;
				}

				float submergedArea = dragAreas[i]*p;

				// drag
				float drag = dotDrag > 0.0f ? improvedDragPart*dotDrag*dotDrag*submergedArea : 0.0f;

				//float pointVelocity2 = v.magnitude;
				//float dragVelocityDelta2 = improvedDragPart * dotDrag * dotDrag * totalArea * -2.0f * forceToVelocity;

				// limit drag to avoid inverting velocity direction
				//if(dragVelocityDelta2 > dotDrag)
				//	drag *= dotDrag / dragVelocityDelta2;

				float t = v.magnitude;
				drag = t != 0.0f ? drag/t : 0.0f;     // normalization factor, not a part of drag equation
				force.x = v.x*drag;
				force.y = v.y*drag;
				force.z = v.z*drag;

				// buoyancy
				if (center.y > polygonCenter.y)
				{
					if (waterElevation > center.y)
					{
						p = polygonVolumes[i];
						force.x += improvedBuoyancyPart.x*p;
						force.y += improvedBuoyancyPart.y*p;
						force.z += improvedBuoyancyPart.z*p;
					}
					else if (waterElevation > polygonCenter.y)
					{
						p = polygonVolumes[i]*(waterElevation - polygonCenter.y)/(center.y - polygonCenter.y);
						force.x += improvedBuoyancyPart.x*p;
						force.y += improvedBuoyancyPart.y*p;
						force.z += improvedBuoyancyPart.z*p;
					}
				}
				else if (waterElevation > polygonCenter.y)
				{
					p = polygonVolumes[i];
					force.x += improvedBuoyancyPart.x*p;
					force.y += improvedBuoyancyPart.y*p;
					force.z += improvedBuoyancyPart.z*p;
				}
				else if (waterElevation > center.y)
				{
					p = polygonVolumes[i]*(waterElevation - center.y)/(polygonCenter.y - center.y);
					force.x += improvedBuoyancyPart.x*p;
					force.y += improvedBuoyancyPart.y*p;
					force.z += improvedBuoyancyPart.z*p;
				}

				// flow
				if (dotFlow > 0.0f)
				{
					t = flowForce.magnitude;
					float flow = t != 0.0f ? dotFlow*submergedArea/t : 0.0f;     // flowForce.magnitude is a normalization factor, not a part of flow equation
					force.x += flowForce.x*flow;
					force.y += flowForce.y*flow;
					force.z += flowForce.z*flow;
				}

				rigidBody.AddForceAtPosition(force, polygonCenter, ForceMode.Force);
			}

			averageWaterElevation /= dragNormals.Length;
		}

		private static float SingleComponentTransform(ref Vector3 point, ref Vector4 row)
		{
			return point.x * row.x + point.y * row.y + point.z * row.z + row.w;
		}

		private void CreateWaterProbe()
		{
			if(waterProbe == null)
			{
				waterProbe = WaterVolumeProbe.CreateProbe(rigidBody.transform, localCollider.bounds.extents.magnitude);
				waterProbe.Enter.AddListener(OnWaterEnter);
				waterProbe.Leave.AddListener(OnWaterLeave);
			}
		}

		private void DestroyWaterProbe()
		{
			if(waterProbe != null)
			{
				waterProbe.gameObject.Destroy();
				waterProbe = null;
			}
		}

		private void OnWaterEnter()
		{
			CreateWaterSamplers();
			AffectingWater.ProfilesManager.ValidateProfiles();
			PrecomputeBuoyancy();
			PrecomputeDrag();
			PrecomputeFlow();
        }

		private void OnWaterLeave()
		{
			if(samples != null)
			{
				for(int i = 0; i < sampleCount; ++i)
					samples[i].Stop();

				samples = null;
			}
		}

		private bool ValidateForEditor()
		{
			if(localCollider == null)
			{
				localCollider = GetComponent<Collider>();
				rigidBody = GetComponentInParent<Rigidbody>();
				OnValidate();
			}

			return localCollider != null && rigidBody != null;
		}

		private void PrecomputeSamples()
		{
			var samplePositions = new List<Vector3>();

			float offset = 0.5f;
			float step = 1.0f;
			int targetPoints = sampleCount * 18;
			var transform = this.transform;

			Vector3 min, max;
			ColliderExtensions.GetLocalMinMax(localCollider, out min, out max);

			for(int i = 0; i < 4 && samplePositions.Count < targetPoints; ++i)
			{
				for(float x = offset; x <= 1.0f; x += step)
				{
					for(float y = offset; y <= 1.0f; y += step)
					{
						for(float z = offset; z <= 1.0f; z += step)
						{
							Vector3 p = new Vector3(Mathf.Lerp(min.x, max.x, x), Mathf.Lerp(min.y, max.y, y), Mathf.Lerp(min.z, max.z, z));

							if(localCollider.IsPointInside(transform.TransformPoint(p)))
								samplePositions.Add(p);
						}
					}
				}

				step = offset;
				offset *= 0.5f;
			}

			cachedSamplePositions = samplePositions.ToArray();
			cachedSampleCount = cachedSamplePositions.Length;
			Shuffle(cachedSamplePositions);
		}

		private void PrecomputeImprovedDrag()
		{
			var meshCollider = (MeshCollider)localCollider;
			var mesh = meshCollider.sharedMesh;
			var vertices = mesh.vertices;
			var normals = mesh.normals;
			var indices = mesh.GetIndices(0);

			int numPolygons = indices.Length/3;

			dragNormals = new Vector3[numPolygons];
			dragVertices = new Vector3[numPolygons*3];
			dragCenters = new Vector3[numPolygons];
			dragAreas = new float[numPolygons];
			polygonVolumes = new float[numPolygons];
			Vector3 center = localCollider.transform.InverseTransformPoint(localCollider.bounds.center);

			int index = 0;

			for(int i = 0; i < indices.Length;)
			{
				Vector3 a = vertices[indices[i]];
				Vector3 b = vertices[indices[i+1]];
				Vector3 c = vertices[indices[i+2]];

				dragVertices[i] = a;
				dragVertices[i+1] = b;
				dragVertices[i+2] = c;

				dragAreas[index] = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
				dragCenters[index] = (a + b + c) * 0.333333333f;

				Vector3 na = normals[indices[i++]];
				Vector3 nb = normals[indices[i++]];
				Vector3 nc = normals[indices[i++]];
				
				dragNormals[index] = (na + nb + nc) * 0.333333333f;

				Vector3 p1 = a - center;
				Vector3 p2 = b - center;
				Vector3 p3 = c - center;

				polygonVolumes[index++] = Mathf.Abs(ColliderExtensions.SignedVolumeOfTriangle(p1, p2, p3));		// improved physics are meant only for concave colliders, so we don't need a sign here
			}

			//OptimizeImprovedDrag();
			//Debug.Log("Successfully removed " + (numPolygons - dragNormals.Length) + " unnecessary polygons from " + name + ".");

			improvedDragSamples = new WaterSample[numPolygons];
		}
		
		private void UpdateTotalArea()
		{
			var rigidBody = GetComponentInParent<Rigidbody>();
			var waterPhysics = rigidBody.GetComponentsInChildren<WaterPhysics>();

			totalArea = 0.0f;

			for(int i = 0; i < waterPhysics.Length; ++i)
			{
				var target = waterPhysics[i];

				if(target.GetComponentInParent<Rigidbody>() != rigidBody)
					continue;

				if(target.area == -1.0f && target.localCollider != null)
					target.area = target.localCollider.ComputeArea();

				totalArea += target.area;
			}

			for(int i = 0; i < waterPhysics.Length; ++i)
				waterPhysics[i].totalArea = totalArea;
		}

		private void CreateWaterSamplers()
		{
			var affectingWater = AffectingWater;

			if (useImprovedDragAndFlowForces)
			{
				for (int i = 0; i < improvedDragSamples.Length; ++i)
				{
					improvedDragSamples[i] = new WaterSample(affectingWater, WaterSample.DisplacementMode.HeightAndForces, precision);
					improvedDragSamples[i].Start(transform.TransformPoint(dragCenters[i]));
				}
			}
			else
			{
				if(samples == null || samples.Length != sampleCount)
					samples = new WaterSample[sampleCount];

				for(int i = 0; i < sampleCount; ++i)
				{
					samples[i] = new WaterSample(affectingWater, WaterSample.DisplacementMode.HeightAndForces, precision);
					samples[i].Start(transform.TransformPoint(cachedSamplePositions[cachedSampleIndex]));

					if(++cachedSampleIndex >= cachedSampleCount)
						cachedSampleIndex = 0;
				}
			}
		}

		private void PrecomputeBuoyancy()
		{
			buoyancyPart = -Physics.gravity * (numSamplesInv * volume * buoyancyIntensity * AffectingWater.Density);
			improvedBuoyancyPart = -Physics.gravity * (buoyancyIntensity * AffectingWater.Density);
		}

		private void PrecomputeDrag()
		{
			useCheapDrag = dragCoefficient > 0.0f && !useImprovedDragAndFlowForces;
			dragPart = 0.5f * dragCoefficient * area * numSamplesInv * AffectingWater.Density;
			improvedDragPart = -0.5f*dragCoefficient*AffectingWater.Density;
		}

		private void PrecomputeFlow()
		{
			useCheapFlow = flowIntensity > 0.0f && !useImprovedDragAndFlowForces;
			flowPart = flowIntensity * dragCoefficient * area * numSamplesInv * 100.0f;
			improvedFlowPart = flowIntensity*dragCoefficient*-100.0f;			// minus here negates the normal in the main equation
		}

		private static void Shuffle<T>(T[] array)
		{
			int n = array.Length;

			while(n > 1)
			{
				int k = Random.Range(0, n--);

				var t = array[n];
				array[n] = array[k];
				array[k] = t;
			}
		}

		private struct NormalMatch
		{
			public readonly ushort indexA, indexB;
			public float match;

			public NormalMatch(ushort indexA, ushort indexB, float match)
			{
				this.indexA = indexA;
				this.indexB = indexB;
				this.match = match;
			}
		}
	}
}
