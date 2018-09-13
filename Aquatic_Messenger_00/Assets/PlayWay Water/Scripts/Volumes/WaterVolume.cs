using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	[System.Serializable]
	public class WaterVolume
	{
		[Tooltip("Makes water volume be infinite in horizontal directions and infinitely deep. It is still reduced by substractive colliders tho. Check that if this is an ocean, sea or if this water spans through most of the scene. If you will uncheck this, you will need to add some child colliders to define where water should display.")]
		[SerializeField]
		private bool boundless = true;
		
		private bool collidersAdded;
		private Water water;

		private readonly List<WaterVolumeAdd> volumes = new List<WaterVolumeAdd>();
		private readonly List<WaterVolumeSubtract> subtractors = new List<WaterVolumeSubtract>();

		internal void Start(Water water)
		{
			this.water = water;
		}

		public bool Boundless
		{
			get { return boundless; }
		}

		public bool HasRenderableAdditiveVolumes
		{
			get
			{
				for(int i = volumes.Count - 1; i >= 0; --i)
				{
					if(volumes[i].RenderMode != WaterVolumeRenderMode.None)
						return true;
				}

				return false;
			}
		}

		public List<WaterVolumeAdd> GetVolumesDirect()
		{
			return volumes;
		}

		public List<WaterVolumeSubtract> GetSubtractiveVolumesDirect()
		{
			return subtractors;
		}

		public void Dispose()
		{
			
        }

		internal void Enable()
		{
			if(!collidersAdded && Application.isPlaying)
			{
				var colliders = water.GetComponentsInChildren<Collider>(true);

				for(int i=0; i<colliders.Length; ++i)
				{
					var collider = colliders[i];
					var volumeSubtract = collider.GetComponent<WaterVolumeSubtract>();

					if (volumeSubtract == null)
					{
						var volumeAdd = collider.GetComponent<WaterVolumeAdd>();
						AddVolume(volumeAdd != null ? volumeAdd : collider.gameObject.AddComponent<WaterVolumeAdd>());
					}
				}

				collidersAdded = true;
            }

			EnableRenderers();
		}

		internal void Disable()
		{
			Dispose();
			DisableRenderers();
		}

		public void EnableRenderers()
		{
			for(int i = 0; i < volumes.Count; ++i)
				volumes[i].EnableRenderers(false);

			for(int i = 0; i < subtractors.Count; ++i)
				subtractors[i].EnableRenderers(false);
		}

		public void DisableRenderers()
		{
			for(int i = 0; i < volumes.Count; ++i)
				volumes[i].DisableRenderers();

			for(int i = 0; i < subtractors.Count; ++i)
				subtractors[i].DisableRenderers();
		}

		internal void AddVolume(WaterVolumeAdd volume)
		{
			volumes.Add(volume);
            volume.AssignTo(water);
		}

		internal void RemoveVolume(WaterVolumeAdd volume)
		{
			volumes.Remove(volume);
		}

		internal void AddSubtractor(WaterVolumeSubtract volume)
		{
			subtractors.Add(volume);
			volume.AssignTo(water);
		}

		internal void RemoveSubtractor(WaterVolumeSubtract volume)
		{
			subtractors.Remove(volume);
		}

		public bool IsPointInside(Vector3 point, WaterVolumeSubtract[] exclusions, float radius = 0.0f)
		{
			for(int i=subtractors.Count-1; i>=0; --i)
			{
				var volume = subtractors[i];

				if(volume.EnablePhysics && volume.IsPointInside(point) && !Contains(exclusions, volume))
					return false;
			}

			if(boundless)
				return point.y - radius <= water.transform.position.y + water.MaxVerticalDisplacement;

			for(int i=volumes.Count-1; i>=0; --i)
			{
				var volume = volumes[i];

				if(volume.EnablePhysics && volume.IsPointInside(point))
					return true;
			}

			return false;
		}

		private static bool Contains(WaterVolumeSubtract[] array, WaterVolumeSubtract element)
		{
			if(array == null) return false;

			for(int i = array.Length - 1; i >= 0; --i)
			{
				if(array[i] == element)
					return true;
			}

			return false;
		}

		internal bool IsPointInsideMainVolume(Vector3 point, float radius = 0.0f)
		{
			if(boundless)
				return point.y - radius <= water.transform.position.y + water.MaxVerticalDisplacement;
			else
				return false;
		}
	}
}
