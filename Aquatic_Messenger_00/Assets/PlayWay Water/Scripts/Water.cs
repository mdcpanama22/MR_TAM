using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace PlayWay.Water
{
	/// <summary>
	///     Main water component.
	/// </summary>
	[ExecuteInEditMode]
	[AddComponentMenu("Water/Water (Base Component)", -1)]
	public sealed class Water : MonoBehaviour, ISerializationCallbackReceiver
	{
		[SerializeField]
		[FormerlySerializedAs("shaderCollection")]
		private ShaderSet shaderSet;

		[Tooltip("Set it to anything else than 0 if your game has multiplayer functionality or you want your water to behave the same way each time your game is played (good for intro etc.).")]
		[SerializeField]
		private int seed;

		[SerializeField]
		private WaterMaterials materials;

		[SerializeField]
		private ProfilesManager profilesManager;

		[SerializeField]
		private WaterGeometry geometry;

		[SerializeField]
		private WaterRenderer waterRenderer;

		[SerializeField]
		private WaterUvAnimator uvAnimator;

		[SerializeField]
		private WaterVolume volume;

		[SerializeField]
		private WaterSubsurfaceScattering subsurfaceScattering;

		[SerializeField]
		private DynamicWater.Data dynamicWaterData;

		[SerializeField]
		private Foam.Data foamData;

		[SerializeField]
		private PlanarReflection.Data planarReflectionData;

		[SerializeField]
		private WindWaves.Data windWavesData;

		[SerializeField]
		private bool dontRotateUpwards;

		[SerializeField]
		private bool fastEnableDisable;

		[SerializeField]
#pragma warning disable 0414
		private float version = 2.0f;

		// used only by the water updater
		#region Deprecated fields
		[Obsolete]
		[SerializeField]
		private bool refraction = true;

		[Obsolete]
		[SerializeField]
		private bool blendEdges = true;

		[Obsolete]
		[SerializeField]
		private bool receiveShadows;

		[Obsolete]
		[SerializeField]
		private float tesselationFactor = 1.0f;

		[Obsolete]
		[SerializeField]
		private bool useCubemapReflections = true;
		#endregion
#pragma warning restore 0414

		private DynamicWater dynamicWater;
		private Foam foam;
		private PlanarReflection planarReflection;
		private WindWaves windWaves;

		private bool componentsCreated;
		private int waterId = -1;
		private int activeSamplesCount;
		private Vector2 surfaceOffset = new Vector2(float.NaN, float.NaN);
		private float maxHorizontalDisplacement;
		private float maxVerticalDisplacement;
		private float time = -1.0f;
		private float density;
		private float gravity;
		private bool renderingEnabled = true;

		public event Action WaterIdChanged;

		private static bool isPlaying;
		private static int nextWaterId = 256;
		private static readonly bool[] idUsageRegister = new bool[256];

		public static Water CreateWater(string name, ShaderSet shaderCollection)
		{
			var gameObject = new GameObject(name);
			var water = gameObject.AddComponent<Water>();
			water.shaderSet = shaderCollection;
			return water;
		}

		private void Awake()
		{
			WaterQualitySettings.Instance.Changed -= OnQualitySettingsChanged;
			WaterQualitySettings.Instance.Changed += OnQualitySettingsChanged;
		}

		private void OnEnable()
		{
			if(fastEnableDisable && componentsCreated)
				return;

			isPlaying = Application.isPlaying;          // can't access it from OnAfterDeserialize in other way

			CreateWaterComponents();
			AssignId();

			materials.Enable();
			profilesManager.Enable();
			geometry.Enable();
			waterRenderer.Enable();
			volume.Enable();
			subsurfaceScattering.Enable();

			if (foam != null) foam.Enable();
			if (dynamicWater != null) dynamicWater.Enable();
			if (windWaves != null) windWaves.Enable();

			if(renderingEnabled)
				WaterGlobals.Instance.AddWater(this);
		}

		private void OnDisable()
		{
			if(fastEnableDisable)
				return;

			FreeId();
			WaterGlobals.Instance.RemoveWater(this);

			materials.Disable();
			profilesManager.Disable();
			geometry.Disable();
			waterRenderer.Disable();
			volume.Disable();
			subsurfaceScattering.Disable();

			if (foam != null) foam.Disable();
			if (dynamicWater != null) dynamicWater.Disable();
			if (windWaves != null) windWaves.Disable();
		}

		private void OnDestroy()
		{
			if (fastEnableDisable)
			{
				fastEnableDisable = false;
				OnDisable();
			}

			WaterQualitySettings.Instance.Changed -= OnQualitySettingsChanged;

			subsurfaceScattering.Destroy();
			materials.Destroy();
			profilesManager.Destroy();

			if (dynamicWater != null)
			{
				dynamicWater.Destroy();
				dynamicWater = null;
			}

			if (foam != null)
			{
				foam.Destroy();
				foam = null;
			}

			if (planarReflection != null)
			{
				planarReflection.Destroy();
				planarReflection = null;
			}

			if (windWaves != null)
			{
				windWaves.Destroy();
				windWaves = null;
			}
		}

		public WaterMaterials Materials
		{
			get { return materials; }
		}

		public ProfilesManager ProfilesManager
		{
			get { return profilesManager; }
		}

		public WaterGeometry Geometry
		{
			get { return geometry; }
		}

		public WaterRenderer Renderer
		{
			get { return waterRenderer; }
		}

		public WaterUvAnimator UVAnimator
		{
			get { return uvAnimator; }
		}

		public WaterVolume Volume
		{
			get { return volume; }
			set { volume = value; }
		}

		public DynamicWater DynamicWater
		{
			get { return dynamicWater; }
		}

		public Foam Foam
		{
			get { return foam; }
		}

		public PlanarReflection PlanarReflection
		{
			get { return planarReflection; }
		}

		public WaterSubsurfaceScattering SubsurfaceScattering
		{
			get { return subsurfaceScattering; }
		}

		public WindWaves WindWaves
		{
			get { return windWaves; }
		}

		public ShaderSet ShaderSet
		{
			get
			{
#if UNITY_EDITOR
				if(shaderSet == null)
					shaderSet = WaterPackageUtilities.FindDefaultAsset<ShaderSet>("\"Ocean\" t:ShaderSet", "t:ShaderSet");
#endif

				return shaderSet;
			}
		}

		/// <summary>
		///		Use this property instead of disabling water completely, if you want to either:
		///			- temporarily disable water from rendering without releasing its resources so that enabling it again won't cause hiccups,
		///			- disable water from rendering, but keep the physics.
		/// </summary>
		public bool RenderingEnabled
		{
			get { return renderingEnabled; }
			set
			{
				if(renderingEnabled == value)
					return;

				renderingEnabled = value;

				if (renderingEnabled)
				{
					if(enabled)
						WaterGlobals.Instance.AddWater(this);
				}
				else
					WaterGlobals.Instance.RemoveWater(this);
			}
		}

		public int ComputedSamplesCount
		{
			get { return activeSamplesCount; }
		}

		public float Density
		{
			get { return density; }
		}

		public float Gravity
		{
			get { return gravity; }
		}

		public float MaxHorizontalDisplacement
		{
			get { return maxHorizontalDisplacement; }
		}

		public float MaxVerticalDisplacement
		{
			get { return maxVerticalDisplacement; }
		}

		public int Seed
		{
			get { return seed; }
			set { seed = value; }
		}

		public Vector2 SurfaceOffset
		{
			get { return float.IsNaN(surfaceOffset.x) ? new Vector2(-transform.position.x, -transform.position.z) : surfaceOffset; }
			set { surfaceOffset = value; }
		}

		public float Time
		{
			get { return time == -1 ? UnityEngine.Time.time : time; }
			set { time = value; }
		}

		public float UniformWaterScale
		{
			get { return transform.localScale.y; }
		}

		public int WaterId
		{
			get { return waterId; }
		}

		#region Elevation sampling

		/// <summary>
		/// Computes water displacement vector at a given coordinates. WaterSample class does the same thing asynchronously and is recommended.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="z"></param>
		/// <param name="spectrumStart"></param>
		/// <param name="spectrumEnd"></param>
		/// <param name="time"></param>
		/// <returns></returns>
		public Vector3 GetDisplacementAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			Vector3 result = new Vector3();

			if(windWaves != null)
				result = windWaves.GetDisplacementAt(x, z, spectrumStart, spectrumEnd, time);

			return result;
		}

		/// <summary>
		/// Computes horizontal displacement vector at a given coordinates. WaterSample class does the same thing asynchronously and is recommended.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="z"></param>
		/// <param name="spectrumStart"></param>
		/// <param name="spectrumEnd"></param>
		/// <param name="time"></param>
		/// <returns></returns>
		public Vector2 GetHorizontalDisplacementAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			Vector2 result = new Vector2();

			if(windWaves != null)
				result = windWaves.GetHorizontalDisplacementAt(x, z, spectrumStart, spectrumEnd, time);

			return result;
		}

		/// <summary>
		/// Computes height at a given coordinates. WaterSample class does the same thing asynchronously and is recommended.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="z"></param>
		/// <param name="spectrumStart"></param>
		/// <param name="spectrumEnd"></param>
		/// <param name="time"></param>
		/// <returns></returns>
		public float GetHeightAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			float result = 0.0f;

			if(windWaves != null)
				result = windWaves.GetHeightAt(x, z, spectrumStart, spectrumEnd, time);

			return result;
		}

		/// <summary>
		/// Computes forces and height at a given coordinates. WaterSample class does the same thing asynchronously and is recommended.
		/// </summary>
		/// <param name="x"></param>
		/// <param name="z"></param>
		/// <param name="spectrumStart"></param>
		/// <param name="spectrumEnd"></param>
		/// <param name="time"></param>
		/// <returns></returns>
		public Vector4 GetHeightAndForcesAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			Vector4 result = Vector4.zero;

			if(windWaves != null)
				result = windWaves.GetForceAndHeightAt(x, z, spectrumStart, spectrumEnd, time);

			return result;
		}

		#endregion

		#region Deprecated members

		[Obsolete("Please use ProfilesManager.Profiles instead.")]
		public WeightedProfile[] Profiles
		{
			get { return profilesManager.Profiles; }
		}

		[Obsolete("Please use ProfilesManager.Changed instead.")]
		public WaterEvent ProfilesChanged
		{
			get { return profilesManager.Changed; }
		}

		[Obsolete("Please use ProfilesManager.CacheProfiles instead.")]
		public void CacheProfiles(params WaterProfile[] profiles)
		{
			this.profilesManager.CacheProfiles(profiles);
		}

		[Obsolete("Please use ProfilesManager.SetProfiles instead.")]
		public void SetProfiles(params WeightedProfile[] profiles)
		{
			this.profilesManager.SetProfiles(profiles);
		}

		#endregion

		/// <summary>
		/// Use this in your custom script that tries to access water very early after a scene gets loaded.
		/// </summary>
		public void ForceStartup()
		{
			CreateWaterComponents();
		}

		public void OnBeforeSerialize()
		{
			
		}

		public void OnAfterDeserialize()
		{
			if(!isPlaying)
				componentsCreated = false;
		}

		public void ResetWater()
		{
			enabled = false;
			OnDestroy();
			componentsCreated = false;
			enabled = true;
		}

		internal void OnWaterRender(Camera camera)
		{
			if (!isActiveAndEnabled) return;

			materials.OnWaterRender(camera);

			if (dynamicWater != null) dynamicWater.OnWaterRender(camera);
			if (planarReflection != null) planarReflection.OnWaterRender(camera);
			if (windWaves != null) windWaves.OnWaterRender(camera);

			subsurfaceScattering.OnWaterRender(camera);
		}

		internal void OnWaterPostRender(Camera camera)
		{
			if (planarReflection != null) planarReflection.OnWaterPostRender(camera);
		}

		internal void OnSamplingStarted()
		{
			++activeSamplesCount;
		}

		internal void OnSamplingStopped()
		{
			--activeSamplesCount;
		}

		private void Update()
		{
			profilesManager.Update();

			if (!Application.isPlaying) return;

			if (!dontRotateUpwards)
				transform.eulerAngles = new Vector3(0.0f, transform.eulerAngles.y, 0.0f);

			UpdateStatisticalData();

			uvAnimator.Update();
			geometry.Update();

			if (dynamicWater != null) dynamicWater.Update();
			if (planarReflection != null) planarReflection.Update();
			if (windWaves != null) windWaves.Update();
			if (foam != null) foam.Update();
		}

		public void OnValidate()
		{
			if (!componentsCreated && isActiveAndEnabled)
			{
				bool enableComponents = enabled && Application.isPlaying;

				if (enableComponents)
					OnDisable();

				CreateWaterComponents();

				if (enableComponents)
					OnEnable();
			}

			if (componentsCreated)
			{
				materials.Validate();
				profilesManager.Validate();
				waterRenderer.Validate();
				geometry.Validate();
				subsurfaceScattering.Validate();

				if (dynamicWater != null) dynamicWater.Validate();
				if (planarReflection != null) planarReflection.Validate();
				if (windWaves != null) windWaves.Validate();
				if (foam != null) foam.Validate();
			}
		}

		/// <summary>
		/// Creates some internal management classes, depending if they are needed by the used shader collection.
		/// </summary>
		private void CreateWaterComponents()
		{
			if(componentsCreated)
				return;

			componentsCreated = true;

			if(materials == null)
			{
				materials = new WaterMaterials();
				profilesManager = new ProfilesManager();
				waterRenderer = new WaterRenderer();
				uvAnimator = new WaterUvAnimator();
				volume = new WaterVolume();
				geometry = new WaterGeometry();
				subsurfaceScattering = new WaterSubsurfaceScattering();
			}

			profilesManager.Start(this);
			materials.Start(this);
			waterRenderer.Start(this);
			uvAnimator.Start(this);
			volume.Start(this);
			geometry.Start(this);
			subsurfaceScattering.Start(this);

			profilesManager.Changed.AddListener(OnProfilesChanged);

			if(shaderSet.LocalEffectsSupported)
				dynamicWater = new DynamicWater(this, dynamicWaterData);
			
			if(shaderSet.PlanarReflections != PlanarReflectionsMode.Disabled)
				planarReflection = new PlanarReflection(this, planarReflectionData);

			if(shaderSet.WindWavesMode != WindWavesRenderMode.Disabled)
				windWaves = new WindWaves(this, windWavesData);

			if(shaderSet.Foam)
				foam = new Foam(this, foamData);			// has to be after wind waves
		}

		private void OnProfilesChanged(Water water)
		{
			var profiles = water.ProfilesManager.Profiles;

			density = 0.0f;
			gravity = 0.0f;

			for (int i = 0; i < profiles.Length; ++i)
			{
				var profile = profiles[i].Profile;
				float weight = profiles[i].Weight;

				density += profile.Density * weight;
				gravity -= profile.Gravity * weight;
			}
		}

		private void OnQualitySettingsChanged()
		{
			OnValidate();
		}

		private void UpdateStatisticalData()
		{
			maxHorizontalDisplacement = 0.0f;
			maxVerticalDisplacement = 0.0f;

			if(windWaves != null)
			{
				maxHorizontalDisplacement = windWaves.MaxHorizontalDisplacement;
				maxVerticalDisplacement = windWaves.MaxVerticalDisplacement;
			}
		}

		private void AssignId()
		{
			if(waterId != -1)
				return;         // already assigned

			for(waterId = 1; waterId < 256; ++waterId)
			{
				if(!idUsageRegister[waterId])
				{
					idUsageRegister[waterId] = true;
					break;
				}
			}

			if(waterId == 256)
				waterId = nextWaterId++;

			if(WaterIdChanged != null)
				WaterIdChanged();
		}

		private void FreeId()
		{
			if(waterId == -1)
				return;				// already freed

			if(waterId < idUsageRegister.Length)
				idUsageRegister[waterId] = false;

			waterId = -1;
		}

		private static readonly Collider[] collidersBuffer = new Collider[30];
		private static readonly List<Water> possibleWaters = new List<Water>();
		private static readonly List<Water> excludedWaters = new List<Water>();

		public static Water FindWater(Vector3 position, float radius)
		{
			bool unused1, unused2;
			return FindWater(position, radius, null, out unused1, out unused2);
		}

		public static Water FindWater(Vector3 position, float radius, out bool isInsideSubtractiveVolume, out bool isInsideAdditiveVolume)
		{
			return FindWater(position, radius, null, out isInsideSubtractiveVolume, out isInsideAdditiveVolume);
		}

		public static Water FindWater(Vector3 position, float radius, List<Water> allowedWaters, out bool isInsideSubtractiveVolume, out bool isInsideAdditiveVolume)
		{
			isInsideSubtractiveVolume = false;
			isInsideAdditiveVolume = false;

#if UNITY_5_2 || UNITY_5_1 || UNITY_5_0
			var collidersBuffer = Physics.OverlapSphere(position, radius, 1 << WaterProjectSettings.Instance.WaterCollidersLayer, QueryTriggerInteraction.Collide);
			int numHits = collidersBuffer.Length;
#else
			int numHits = Physics.OverlapSphereNonAlloc(position, radius, collidersBuffer, 1 << WaterProjectSettings.Instance.WaterCollidersLayer, QueryTriggerInteraction.Collide);
#endif

			possibleWaters.Clear();
			excludedWaters.Clear();

			for(int i = 0; i < numHits; ++i)
			{
				var volume = WaterVolumeBase.GetWaterVolume(collidersBuffer[i]);

				if(volume != null)
				{
					if(volume is WaterVolumeAdd)
					{
						isInsideAdditiveVolume = true;

						if(allowedWaters == null || allowedWaters.Contains(volume.Water))
							possibleWaters.Add(volume.Water);
					}
					else                // subtractive
					{
						isInsideSubtractiveVolume = true;
						excludedWaters.Add(volume.Water);
					}
				}
			}

			for(int i = 0; i < possibleWaters.Count; ++i)
			{
				if(!excludedWaters.Contains(possibleWaters[i]))
					return possibleWaters[i];
			}

			var boundlessWaters = WaterGlobals.Instance.BoundlessWaters;
			int numBoundlessWaters = boundlessWaters.Count;

			for(int i = 0; i < numBoundlessWaters; ++i)
			{
				var water = boundlessWaters[i];

				if((allowedWaters == null || allowedWaters.Contains(water)) && water.Volume.IsPointInsideMainVolume(position, radius) && !excludedWaters.Contains(water))
					return water;
			}

			return null;
		}

		public struct WeightedProfile
		{
			public readonly WaterProfile Profile;
			public readonly float Weight;

			public WeightedProfile(WaterProfile profile, float weight)
			{
				Profile = profile;
				Weight = weight;
			}
		}

		[Serializable]
		public class WaterEvent : UnityEvent<Water> { };
	}
}
