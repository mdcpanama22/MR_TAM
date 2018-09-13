using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	[ExecuteInEditMode]
	public abstract class WaterVolumeBase : MonoBehaviour
	{
		[SerializeField]
		private Water water;

		[SerializeField]
		private WaterVolumeRenderMode renderMode = WaterVolumeRenderMode.Basic;

		[SerializeField]
		private bool affectPhysics = true;

		[HideInInspector]
		[System.Obsolete("Please use renderMode property instead.")]
		[Tooltip("Renders water around collider below water level. Example of use may be a glass bottle displacing sea water. If you will not use this and still use subtractive volumes, there will be a hole in the water behind the bottle.\n\nIn case of subtractive volumes this will be buggy unless \"WindWaves / FFT / Flatten Mode\" is set to \"Forced On\".")]
		[SerializeField]
		private bool renderDisplacedWater;
		
		[HideInInspector]
		[System.Obsolete("Please use renderMode and affectPhysics properties instead.")]
        [SerializeField]
#pragma warning disable 618
		private WaterVolumeMode mode = WaterVolumeMode.Deprecated;
#pragma warning restore 618

		private Collider[] colliders;
		private MeshRenderer[] volumeRenderers;

		private static readonly Dictionary<Collider, WaterVolumeBase> colliderToVolumeCache = new Dictionary<Collider, WaterVolumeBase>();

		protected void OnEnable()
		{
			colliders = GetComponents<Collider>();
			gameObject.layer = WaterProjectSettings.Instance.WaterCollidersLayer;

			Register(water);

			if(renderMode != WaterVolumeRenderMode.None && water != null && Application.isPlaying)
				CreateRenderers();
		}

		protected void OnDisable()
		{
			DisposeRenderers();
			Unregister(water);
		}

		public Water Water
		{
			get { return water; }
		}

		public bool EnablePhysics
		{
			get { return affectPhysics; }
		}

		public WaterVolumeRenderMode RenderMode
		{
			get { return renderMode; }
		}
		
		public MeshRenderer[] VolumeRenderers
		{
			get { return volumeRenderers; }
		}

		protected virtual CullMode CullMode
		{
			get { return CullMode.Back; }
		}

		protected abstract void Register(Water water);
		protected abstract void Unregister(Water water);

		public static WaterVolumeBase GetWaterVolume<T>(Collider collider) where T : WaterVolumeBase
		{
			return GetWaterVolume(collider) as T;
		}

		public static WaterVolumeBase GetWaterVolume(Collider collider)
		{
			WaterVolumeBase volume;

			if(!colliderToVolumeCache.TryGetValue(collider, out volume))
			{
				volume = collider.GetComponent<WaterVolumeBase>();

				if(volume != null)
					colliderToVolumeCache[collider] = volume;
				else
					// ReSharper disable once RedundantAssignment
					colliderToVolumeCache[collider] = volume = null;         // force null reference (Unity uses custom null)
			}

			return volume;
        }

		protected void OnValidate()
		{
			colliders = GetComponents<Collider>();

			for(int i=0; i<colliders.Length; ++i)
			{
				if(!colliders[i].isTrigger)
					colliders[i].isTrigger = true;
			}

			if (water == null)
				water = GetComponentInChildren<Water>();

#pragma warning disable 0618  // Type or member is obsolete
			if(mode != WaterVolumeMode.Deprecated)
			{
				affectPhysics = true;

				if(mode == WaterVolumeMode.PhysicsAndRendering)
					renderMode = renderDisplacedWater ? WaterVolumeRenderMode.Full : WaterVolumeRenderMode.Basic;
				else
					renderMode = WaterVolumeRenderMode.None;

				mode = WaterVolumeMode.Deprecated;
			}
#pragma warning restore 0618  // Type or member is obsolete
		}

		public void AssignTo(Water water)
		{
			if(this.water == water)
				return;

			DisposeRenderers();
			Unregister(water);
			this.water = water;
			Register(water);

			if(renderMode != WaterVolumeRenderMode.None && water != null && Application.isPlaying)
				CreateRenderers();
		}

		public void EnableRenderers(bool forBorderRendering)
		{
			if(volumeRenderers != null)
			{
				bool enable = (!forBorderRendering || renderMode == WaterVolumeRenderMode.Full) && water.enabled;

				for(int i = 0; i < volumeRenderers.Length; ++i)
					volumeRenderers[i].enabled = enable;
			}
		}

		public void DisableRenderers()
		{
			if(volumeRenderers != null)
			{
				for(int i = 0; i < volumeRenderers.Length; ++i)
					volumeRenderers[i].enabled = false;
			}
		}

		internal void SetLayer(int layer)
		{
			if(volumeRenderers != null)
			{
				for(int i = 0; i < volumeRenderers.Length; ++i)
					volumeRenderers[i].gameObject.layer = layer;
			}
		}

		public bool IsPointInside(Vector3 point)
		{
			for (int i = 0; i < colliders.Length; ++i)
			{
				if (colliders[i].IsPointInside(point))
					return true;
			}

			return false;
		}

		private void Update()
		{
			if (volumeRenderers != null)
			{
				for (int i = 0; i < volumeRenderers.Length; ++i)
					volumeRenderers[i].SetPropertyBlock(water.Renderer.PropertyBlock);
			}
		}

		private void DisposeRenderers()
		{
			if (volumeRenderers != null)
			{
				for (int i = 0; i < volumeRenderers.Length; ++i)
				{
					if (volumeRenderers[i] != null)
						Destroy(volumeRenderers[i].gameObject);
				}

				volumeRenderers = null;
			}
		}

		protected virtual void CreateRenderers()
		{
			int numVolumes = colliders.Length;
			volumeRenderers = new MeshRenderer[numVolumes];

			var material = CullMode == CullMode.Back ? water.Materials.VolumeMaterial : water.Materials.VolumeBackMaterial;

			for(int i = 0; i < numVolumes; ++i)
			{
				var collider = colliders[i];

				GameObject rendererGo;

				if(collider is BoxCollider)
				{
					rendererGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
					rendererGo.transform.localScale = (collider as BoxCollider).size;
				}
				else if(collider is MeshCollider)
				{
					var meshCollider = (MeshCollider)collider;
					var sharedMesh = meshCollider.sharedMesh;

					if (sharedMesh == null)
						throw new System.InvalidOperationException("MeshCollider used to mask water doesn't have a mesh assigned.");

					rendererGo = new GameObject {hideFlags = HideFlags.DontSave};

					var mf = rendererGo.AddComponent<MeshFilter>();
					mf.sharedMesh = sharedMesh;

					rendererGo.AddComponent<MeshRenderer>();
				}
				else if(collider is SphereCollider)
				{
					float d = (collider as SphereCollider).radius * 2;

					rendererGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
					rendererGo.transform.localScale = new Vector3(d, d, d);
				}
				else if(collider is CapsuleCollider)
				{
					var capsuleCollider = collider as CapsuleCollider;
					float height = capsuleCollider.height * 0.5f;
					float radius = capsuleCollider.radius * 2.0f;

					rendererGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);

					switch(capsuleCollider.direction)
					{
						case 0:
						{
							rendererGo.transform.localEulerAngles = new Vector3(0.0f, 0.0f, 90.0f);
							rendererGo.transform.localScale = new Vector3(height, radius, radius);
							break;
						}

						case 1:
						{
							rendererGo.transform.localScale = new Vector3(radius, height, radius);
							break;
						}

						case 2:
						{
							rendererGo.transform.localEulerAngles = new Vector3(90.0f, 0.0f, 0.0f);
							rendererGo.transform.localScale = new Vector3(radius, radius, height);
							break;
						}
					}
				}
				else
					throw new System.InvalidOperationException("Unsupported collider type.");

				rendererGo.hideFlags = HideFlags.DontSave;
				rendererGo.name = "Volume Renderer";
				rendererGo.layer = WaterProjectSettings.Instance.WaterLayer;
				rendererGo.transform.SetParent(transform, false);

				Destroy(rendererGo.GetComponent<Collider>());

				var renderer = rendererGo.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = material;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
#if UNITY_5_4
				renderer.lightProbeUsage = LightProbeUsage.Off;
#else
				renderer.useLightProbes = false;
#endif
				renderer.enabled = false;
				renderer.SetPropertyBlock(water.Renderer.PropertyBlock);

				volumeRenderers[i] = renderer;
			}
		}

		[System.Obsolete("Please use renderMode and affectPhysics properties instead.")]
		public enum WaterVolumeMode
		{
			Physics,
			PhysicsAndRendering,
			Deprecated
		}
	}

	public enum WaterVolumeRenderMode
	{
		None,
		Basic,
		Full
	}
}
