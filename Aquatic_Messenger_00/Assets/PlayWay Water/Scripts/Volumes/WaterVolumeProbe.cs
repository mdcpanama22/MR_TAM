﻿using UnityEngine;
using UnityEngine.Events;

namespace PlayWay.Water
{
	/// <summary>
	///     Finds out in which water volume this GameObject is contained and raises events on enter/leave.
	/// </summary>
	public sealed class WaterVolumeProbe : MonoBehaviour
	{
		[SerializeField]
		private UnityEvent enter;

		[SerializeField]
		private UnityEvent leave;

		private Water currentWater;
		private Transform target;
		private bool targetted;
		private WaterVolumeSubtract[] exclusions;
		private float size;

		public Water CurrentWater
		{
			get { return currentWater; }
		}

		public UnityEvent Enter
		{
			get { return enter ?? (enter = new UnityEvent()); }
		}

		public UnityEvent Leave
		{
			get { return leave ?? (leave = new UnityEvent()); }
		}

		private void Start()
		{
			ScanWaters();
		}

		private void FixedUpdate()
		{
			if(targetted)
			{
				if(target == null)
				{
					Destroy(gameObject);            // cleans itself if target has been destroyed
					return;
				}

				transform.position = target.position;
			}

			if(currentWater != null && currentWater.Volume.Boundless)
			{
				if(!currentWater.Volume.IsPointInsideMainVolume(transform.position) && !currentWater.Volume.IsPointInside(transform.position, exclusions, size))
					LeaveCurrentWater();
            }
			else if(currentWater == null)
				ScanBoundlessWaters();
		}

		private void OnDestroy()
		{
			currentWater = null;

			if(enter != null)
			{
				enter.RemoveAllListeners();
				enter = null;
			}

			if(leave != null)
			{
				leave.RemoveAllListeners();
				leave = null;
			}
		}

		public void OnTriggerEnter(Collider other)
		{
			if(currentWater != null)
			{
				var volumeSubtract = WaterVolumeBase.GetWaterVolume<WaterVolumeSubtract>(other);

				if(volumeSubtract != null && volumeSubtract.EnablePhysics)
				{
					//if(!currentWater.Volume.IsPointInside(transform.position, exclusions, size))
						LeaveCurrentWater();
				}
			}
			else
			{
				var volumeAdd = WaterVolumeBase.GetWaterVolume<WaterVolumeAdd>(other);

				if(volumeAdd != null && volumeAdd.EnablePhysics/* && volumeAdd.Water.Volume.IsPointInside(transform.position, exclusions, size)*/)
					EnterWater(volumeAdd.Water);
			}
        }

		public void OnTriggerExit(Collider other)
		{
			if(currentWater == null)
			{
				var volumeSubtract = WaterVolumeBase.GetWaterVolume<WaterVolumeSubtract>(other);

				if(volumeSubtract != null && volumeSubtract.EnablePhysics)
					ScanWaters();
			}
			else
			{
				var volumeAdd = WaterVolumeBase.GetWaterVolume<WaterVolumeAdd>(other);

				if(volumeAdd != null && volumeAdd.Water == currentWater && volumeAdd.EnablePhysics /* && !currentWater.Volume.IsPointInside(transform.position, exclusions, size)*/)
					LeaveCurrentWater();
			}
		}

		[ContextMenu("Refresh Probe")]
		private void ScanWaters()
		{
			Vector3 position = transform.position;

			var waters = WaterGlobals.Instance.Waters;
			int numWaters = waters.Count;

			for(int i = 0; i < numWaters; ++i)
			{
				if(waters[i].Volume.IsPointInside(position, exclusions, size))
				{
					EnterWater(waters[i]);
					return;
				}
			}

			LeaveCurrentWater();
		}

		private void ScanBoundlessWaters()
		{
			Vector3 position = transform.position;

			var boundlessWaters = WaterGlobals.Instance.BoundlessWaters;
			int numInstances = boundlessWaters.Count;

			for(int i=0; i<numInstances; ++i)
			{
				var water = boundlessWaters[i];

				if(water.Volume.IsPointInsideMainVolume(position) && water.Volume.IsPointInside(position, exclusions, size))
				{
					EnterWater(water);
					return;
				}
			}
		}

		private void EnterWater(Water water)
		{
			if(currentWater == water) return;

			if(currentWater != null)
				LeaveCurrentWater();

			currentWater = water;

			if(enter != null)
				enter.Invoke();
		}

		private void LeaveCurrentWater()
		{
			if(currentWater != null)
			{
				if(leave != null)
					leave.Invoke();

				currentWater = null;
			}
		}

		public static WaterVolumeProbe CreateProbe(Transform target, float size = 0.0f)
		{
			var go = new GameObject("Water Volume Probe") {hideFlags = HideFlags.HideAndDontSave};
			go.transform.position = target.position;
			go.layer = WaterProjectSettings.Instance.WaterCollidersLayer;								// TransparentFX layer by default

			var sphereCollider = go.AddComponent<SphereCollider>();
			sphereCollider.radius = size;
			sphereCollider.isTrigger = true;

			var rigidBody = go.AddComponent<Rigidbody>();
			rigidBody.isKinematic = true;
			rigidBody.mass = 0.0000001f;

			var probe = go.AddComponent<WaterVolumeProbe>();
			probe.target = target;
			probe.targetted = true;
			probe.size = size;
			probe.exclusions = target.GetComponentsInChildren<WaterVolumeSubtract>(true);

			return probe;
		}
	}
}
