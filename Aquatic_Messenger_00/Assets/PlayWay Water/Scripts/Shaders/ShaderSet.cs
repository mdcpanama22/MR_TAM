using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PlayWay.Water
{
	/// <summary>
	///     Stores references to materials with chosen keywords to include them in builds.
	/// </summary>
	[System.Serializable]
	public class ShaderSet : ScriptableObject
	{
		[Header("Reflection & Refraction")]
		[SerializeField]
		private WaterTransparencyMode transparencyMode = WaterTransparencyMode.Refractive;

		[SerializeField]
		private ReflectionProbeUsage reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;

		[SerializeField]
		private PlanarReflectionsMode planarReflections = PlanarReflectionsMode.Normal;

		[Tooltip("Affects direct light specular and diffuse components. Shadows currently work only for main directional light and you need to attach WaterShadowCastingLight script to it. Also it doesn't work at all on mobile platforms.")]
		[SerializeField]
		private bool receiveShadows;

		[Header("Waves")]
		[SerializeField]
		private WindWavesRenderMode windWavesMode = WindWavesRenderMode.FullFFT;

		[SerializeField]
		private DynamicSmoothnessMode dynamicSmoothnessMode = DynamicSmoothnessMode.Physical;

		[SerializeField]
		private bool localEffectsSupported = true;

		[SerializeField]
		private bool localEffectsDebug = false;

		[SerializeField]
		private bool foam = true;

		[Header("Render Modes")]
		[SerializeField]
		private bool forwardRenderMode;

		[SerializeField]
		private bool deferredRenderMode;

		[Header("Geometries Support")]
		[SerializeField]
		private bool projectionGrid = false;

		[SerializeField]
		private bool customTriangularGeometry = false;

		[Header("Volumes")]
		[SerializeField]
		private bool displayOnlyInAdditiveVolumes;

		[SerializeField]
		private bool wavesAlign;

		[Header("Surface")]
		[SerializeField]
		private NormalMappingMode normalMappingMode = NormalMappingMode.Auto;

		[SerializeField]
		private bool supportEmission = false;

		[Header("Generated Shaders")]
		[SerializeField]
		private Shader[] surfaceShaders;

		[SerializeField]
		private Shader[] volumeShaders;

#pragma warning disable 0414
		[SerializeField]
		private Shader[] utilityShaders;
#pragma warning restore 0414

		[SerializeField]
		private ComputeShader[] computeShaders;

#if UNITY_EDITOR
		private bool rebuilding;
		public static IShaderSetBuilder shaderCollectionBuilder;
#endif

		private static bool errorDisplayed;

		public WaterTransparencyMode TransparencyMode
		{
			get { return transparencyMode; }
			set { transparencyMode = value; }
		}

		public ReflectionProbeUsage ReflectionProbeUsage
		{
			get { return reflectionProbeUsage; }
			set { reflectionProbeUsage = value; }
		}

		public bool ReceiveShadows
		{
			get { return receiveShadows; }
			set { receiveShadows = value; }
		}

		public PlanarReflectionsMode PlanarReflections
		{
			get { return planarReflections; }
			set { planarReflections = value; }
		}

		public WindWavesRenderMode WindWavesMode
		{
			get { return windWavesMode; }
			set { windWavesMode = value; }
		}

		public Shader[] SurfaceShaders
		{
			get { return surfaceShaders; }
		}

		public Shader[] VolumeShaders
		{
			get { return volumeShaders; }
		}

		public bool LocalEffectsSupported
		{
			get { return localEffectsSupported; }
		}

		public bool Foam
		{
			get { return foam; }
		}

		public bool LocalEffectsDebug
		{
			get { return localEffectsDebug; }
		}

		public bool CustomTriangularGeometry
		{
			get { return customTriangularGeometry; }
		}

		public bool ProjectionGrid
		{
			get { return projectionGrid; }
		}

		public DynamicSmoothnessMode SmoothnessMode
		{
			get { return dynamicSmoothnessMode; }
		}

#if UNITY_EDITOR
		private static readonly string[] disallowedVolumeKeywords = {
			"_WAVES_FFT_NORMAL", "_WAVES_GERSTNER", "_WATER_FOAM_WS", "_PLANAR_REFLECTIONS", "_PLANAR_REFLECTIONS_HQ",
			"_INCLUDE_SLOPE_VARIANCE", "_NORMALMAP", "_PROJECTION_GRID", "_WATER_OVERLAYS", "_WAVES_ALIGN", "_TRIANGLES",
			"_BOUNDED_WATER"
		};
#endif

		public static Shader GetRuntimeShaderVariant(string keywordsString, bool volume)
		{
			var shader = Shader.Find("PlayWay Water/Variations/Water " + (volume ? "Volume " : "") + keywordsString);

			if(shader == null && !errorDisplayed && Application.isPlaying)
			{
				Debug.LogError("Could not find proper water shader variation. Select your water and click \"Rebuild shaders\" from its context menu to build proper shaders. Missing shader: \"" + "PlayWay Water/Variations/Water " + (volume ? "Volume " : "") + keywordsString + "\"");
				errorDisplayed = true;
			}

			return shader;
		}

		public Shader GetShaderVariant(string[] localKeywords, string[] sharedKeywords, string additionalCode, string keywordsString, bool volume)
		{
			System.Array.Sort(localKeywords);
			System.Array.Sort(sharedKeywords);
			string shaderNameEnd = (volume ? "Volume " : "") + keywordsString;

#if UNITY_EDITOR
			var shaders = volume ? volumeShaders : surfaceShaders;

			if(shaders != null)
			{
				for(int i = 0; i < shaders.Length; ++i)
				{
					var shader = shaders[i];

					if(shader != null && shader.name.EndsWith(shaderNameEnd))
						return shader;                                 // already added
				}
			}

			if(!rebuilding)
			{
				var shader2 = Shader.Find("PlayWay Water/Variations/Water " + shaderNameEnd);

				if(shader2 != null)
				{
					AddShader(shader2, volume);
					return shader2;
				}
			}

			if(shaderCollectionBuilder != null)
			{
				var shader = shaderCollectionBuilder.BuildShaderVariant(localKeywords, sharedKeywords, additionalCode, keywordsString, volume, forwardRenderMode, deferredRenderMode);
				AddShader(shader, volume);

				return shader;
			}

			Assert.IsTrue(false, "Shader Collection Builder is null in editor.");
			return null;
#else
			return Shader.Find("PlayWay Water/Variations/Water " + shaderNameEnd);
#endif
		}

		public void FindBestShaders(out Shader surfaceShader, out Shader volumeShader)
		{
			var variant = new ShaderVariant();
			BuildShaderVariant(variant, WaterQualitySettings.Instance.CurrentQualityLevel);

			var desiredWaterKeywords = variant.GetKeywordsString().Split(' ');

			surfaceShader = null;
			volumeShader = null;

			if (surfaceShaders != null)
			{
				for (int i = 0; i < surfaceShaders.Length; ++i)
				{
					if (surfaceShaders[i] == null)
						continue;

					string shaderName = surfaceShaders[i].name;

					for (int ii = 0; ii < desiredWaterKeywords.Length; ++ii)
					{
						if (shaderName.Contains(desiredWaterKeywords[ii]))
						{
							surfaceShader = surfaceShaders[i];
							break;
						}
					}

					if (surfaceShader != null)
						break;
				}
			}

			if (volumeShaders != null)
			{
				for (int i = 0; i < volumeShaders.Length; ++i)
				{
					if (volumeShaders[i] == null)
						continue;

					string shaderName = volumeShaders[i].name;

					for (int ii = 0; ii < desiredWaterKeywords.Length; ++ii)
					{
						if (shaderName.Contains(desiredWaterKeywords[ii]))
						{
							volumeShader = volumeShaders[i];
							break;
						}
					}

					if (volumeShader != null)
						break;
				}
			}
		}

		[ContextMenu("Rebuild shaders")]
		public void Build()
		{
#if UNITY_EDITOR
			rebuilding = true;
			surfaceShaders = new Shader[0];
			volumeShaders = new Shader[0];

			var qualityLevels = WaterQualitySettings.Instance.GetQualityLevelsDirect();

			for(int i = qualityLevels.Length - 1; i >= 0; --i)
			{
				SetProgress((float)i / qualityLevels.Length);

				var qualityLevel = qualityLevels[i];

				var variant = new ShaderVariant();

				// main shader
				BuildShaderVariant(variant, qualityLevel);

				GetShaderVariant(variant.GetWaterKeywords(), variant.GetUnityKeywords(), variant.GetAdditionalSurfaceCode(), variant.GetKeywordsString(), false);

				//AddFallbackVariants(variant, collection, false, 0);

				SetProgress((i + 0.5f) / qualityLevels.Length);

				// volume shader
				for(int ii = 0; ii < disallowedVolumeKeywords.Length; ++ii)
					variant.SetWaterKeyword(disallowedVolumeKeywords[ii], false);

				GetShaderVariant(variant.GetWaterKeywords(), variant.GetUnityKeywords(), variant.GetAdditionalVolumeCode(), variant.GetKeywordsString(), true);

				//AddFallbackVariants(variant, collection, true, 0);
			}

			SetProgress(1.0f);

			CollectUtilityShaders();
			rebuilding = false;
			ValidateWaterObjects();
#endif
		}

		private static void ValidateWaterObjects()
		{
			var waters = FindObjectsOfType<Water>();

			for(int i = waters.Length - 1; i >= 0; --i)
				waters[i].ResetWater();
		}

		private static void SetProgress(float progress)
		{
#if UNITY_EDITOR
			if(progress != 1.0f)
				EditorUtility.DisplayProgressBar("Building water shaders...", "This may take a minute.", progress);
			else
				EditorUtility.ClearProgressBar();
#endif
		}

		public bool ContainsShaderVariant(string keywordsString)
		{
			if(surfaceShaders != null)
			{
				for(int i = surfaceShaders.Length - 1; i >= 0; --i)
				{
					var shader = surfaceShaders[i];
					if(shader != null && shader.name.EndsWith(keywordsString))
						return true; // already added
				}
			}

			if(volumeShaders != null)
			{
				for(int i = volumeShaders.Length - 1; i >= 0; --i)
				{
					var shader = volumeShaders[i];
					if(shader != null && shader.name.EndsWith(keywordsString))
						return true; // already added
				}
			}

			return false;
		}

		public ComputeShader GetComputeShader(string name)
		{
			for(int i = 0; i < computeShaders.Length; ++i)
			{
				if(computeShaders[i].name.Contains(name))
					return computeShaders[i];
			}

			return null;
		}

#if UNITY_EDITOR
		private void CollectUtilityShaders()
		{
			var shaders = new List<Shader>();

			if(planarReflections != PlanarReflectionsMode.Disabled)
				AddUtilityShader(shaders, "PlayWay Water/Utilities/PlanarReflection - Utilities");

			if(windWavesMode != WindWavesRenderMode.Disabled && windWavesMode != WindWavesRenderMode.Gerstner)
			{
				AddUtilityShader(shaders, "PlayWay Water/Spectrum/Water Spectrum");
				AddUtilityShader(shaders, "PlayWay Water/Base/FFT");
				AddUtilityShader(shaders, "PlayWay Water/Utilities/FFT Utilities");
			}

			if(localEffectsSupported)
			{
				AddUtilityShader(shaders, "PlayWay Water/Utility/Map Local Displacements");
				AddUtilityShader(shaders, "PlayWay Water/Utility/ShorelineMaskRender");
			}

			if(foam)
			{
				AddUtilityShader(shaders, "PlayWay Water/Foam/Global");
				AddUtilityShader(shaders, "PlayWay Water/Foam/Local");
			}

			utilityShaders = shaders.ToArray();

			var computeShaders = new List<ComputeShader>();

			if(windWavesMode != WindWavesRenderMode.Disabled && windWavesMode != WindWavesRenderMode.Gerstner)
				AddComputeShader(computeShaders, "DX11 FFT");

			if(dynamicSmoothnessMode == DynamicSmoothnessMode.Physical)
				AddComputeShader(computeShaders, "Spectral Variances");

			this.computeShaders = computeShaders.ToArray();
		}

		private static void AddUtilityShader(List<Shader> shaders, string name)
		{
			var shader = Shader.Find(name);

			if(shader != null)
			{
				shaders.Add(shader);
			}
			else
				Debug.LogErrorFormat("Your PlayWay Water installation misses shader named \"{0}\". Please reinstall the package.", name);
		}

		private static void AddComputeShader(List<ComputeShader> shaders, string name)
		{
			var guids = AssetDatabase.FindAssets(string.Format("\"{0}\" t:ComputeShader", name));

			if(guids.Length != 0)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[0]);
				var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

				if(computeShader != null)
				{
					shaders.Add(computeShader);
					return;
				}
			}

			Debug.LogErrorFormat("Your PlayWay Water installation misses shader named \"{0}\". Please reinstall the package.", name);
		}
#endif

		private void AddShader(Shader shader, bool volumeShader)
		{
			if(volumeShader)
			{
				if(volumeShaders != null)
				{
					System.Array.Resize(ref volumeShaders, volumeShaders.Length + 1);
					volumeShaders[volumeShaders.Length - 1] = shader;
				}
				else
					volumeShaders = new[] { shader };
			}
			else
			{
				if(surfaceShaders != null)
				{
					System.Array.Resize(ref surfaceShaders, surfaceShaders.Length + 1);
					surfaceShaders[surfaceShaders.Length - 1] = shader;
				}
				else
					surfaceShaders = new[] { shader };
			}
		}

		private void BuildShaderVariant(ShaderVariant variant, WaterQualityLevel qualityLevel)
		{
			bool refraction = transparencyMode == WaterTransparencyMode.Refractive && qualityLevel.allowAlphaBlending;

			variant.SetWaterKeyword("_WATER_REFRACTION", refraction);
			variant.SetWaterKeyword("_CUBEMAP_REFLECTIONS", reflectionProbeUsage != ReflectionProbeUsage.Off);
			variant.SetWaterKeyword("_WATER_RECEIVE_SHADOWS", receiveShadows);

			//variant.SetWaterKeyword("_ALPHATEST_ON", false);
			variant.SetWaterKeyword("_ALPHABLEND_ON", refraction);
			variant.SetWaterKeyword("_ALPHAPREMULTIPLY_ON", !refraction);

			//variant.SetUnityKeyword("_BOUNDED_WATER", !volume.Boundless && volume.HasRenderableAdditiveVolumes);
			variant.SetUnityKeyword("_TRIANGLES", customTriangularGeometry);

			if(projectionGrid)
				variant.SetAdditionalSurfaceCode("_PROJECTION_GRID", "\t\t\t#pragma multi_compile _PROJECTION_GRID_OFF _PROJECTION_GRID");

			variant.SetUnityKeyword("_WATER_OVERLAYS", localEffectsSupported);
			variant.SetUnityKeyword("_LOCAL_MAPS_DEBUG", localEffectsSupported && localEffectsDebug);

			var windWavesRenderMode = BuildWindWavesVariant(variant, qualityLevel);

			variant.SetWaterKeyword("_WATER_FOAM_WS", foam && !localEffectsSupported && windWavesRenderMode == WindWavesRenderMode.FullFFT);
			variant.SetUnityKeyword("_BOUNDED_WATER", displayOnlyInAdditiveVolumes);
			variant.SetUnityKeyword("_WAVES_ALIGN", wavesAlign);

			variant.SetWaterKeyword("_NORMALMAP", normalMappingMode == NormalMappingMode.Always || (normalMappingMode == NormalMappingMode.Auto && windWavesRenderMode > WindWavesRenderMode.GerstnerAndFFTNormals));
			variant.SetWaterKeyword("_EMISSION", supportEmission);
			variant.SetWaterKeyword("_PLANAR_REFLECTIONS", planarReflections == PlanarReflectionsMode.Normal);
			variant.SetWaterKeyword("_PLANAR_REFLECTIONS_HQ", planarReflections == PlanarReflectionsMode.HighQuality);
		}

		private WindWavesRenderMode BuildWindWavesVariant(ShaderVariant variant, WaterQualityLevel qualityLevel)
		{
			WindWavesRenderMode finalWindWavesMode;
			var qualityWindWavesMode = qualityLevel.wavesMode;

			if(windWavesMode == WindWavesRenderMode.Disabled || qualityWindWavesMode == WaterWavesMode.DisallowAll)
				finalWindWavesMode = WindWavesRenderMode.Disabled;
			else if(windWavesMode == WindWavesRenderMode.FullFFT && qualityWindWavesMode == WaterWavesMode.AllowAll)
				finalWindWavesMode = WindWavesRenderMode.FullFFT;
			else if(windWavesMode <= WindWavesRenderMode.GerstnerAndFFTNormals && qualityWindWavesMode <= WaterWavesMode.AllowNormalFFT)
				finalWindWavesMode = WindWavesRenderMode.GerstnerAndFFTNormals;
			else
				finalWindWavesMode = WindWavesRenderMode.Gerstner;

			switch(finalWindWavesMode)
			{
				case WindWavesRenderMode.FullFFT:
				variant.SetUnityKeyword("_WAVES_FFT", true);
				break;

				case WindWavesRenderMode.GerstnerAndFFTNormals:
				variant.SetWaterKeyword("_WAVES_FFT_NORMAL", true);
				variant.SetUnityKeyword("_WAVES_GERSTNER", true);
				break;

				case WindWavesRenderMode.Gerstner:
				variant.SetUnityKeyword("_WAVES_GERSTNER", true);
				break;
			}

			if(dynamicSmoothnessMode == DynamicSmoothnessMode.Physical)
				variant.SetWaterKeyword("_INCLUDE_SLOPE_VARIANCE", true);

			return finalWindWavesMode;
		}

#if UNITY_EDITOR
		public void Clear()
		{
			surfaceShaders = new Shader[0];
			volumeShaders = new Shader[0];
			EditorUtility.SetDirty(this);
		}

		[MenuItem("Assets/Create/Water Shader Set")]
		private static void CreateShaderSet()
		{
			string path = AssetDatabase.GetAssetPath(Selection.activeObject);

			if(string.IsNullOrEmpty(path))
				path = "Assets";
			else if(System.IO.Path.GetExtension(path) != "")
				path = path.Replace(System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");

			var bundle = CreateInstance<ShaderSet>();
			AssetDatabase.CreateAsset(bundle, AssetDatabase.GenerateUniqueAssetPath(path + "/New Shader Collection.asset"));
			AssetDatabase.SaveAssets();

			Selection.activeObject = bundle;
		}
#endif
	}

	public enum WaterTransparencyMode
	{
		Solid,
		Refractive
	}

	/// <summary>
	/// Duplicates UnityEngine ReflectionProbeUsage enum, because it is available only on Unity 5.4+.
	/// </summary>
	public enum ReflectionProbeUsage
	{
		Off,
		BlendProbes,
		BlendProbesAndSkybox,
		Simple,
	}

	public enum PlanarReflectionsMode
	{
		Disabled,
		Normal,
		HighQuality
	}

	public enum DynamicSmoothnessMode
	{
		CheapApproximation,
		Physical
	}

	public enum NormalMappingMode
	{
		/// <summary>
		/// Normal maps are not supported.
		/// </summary>
		Never,

		/// <summary>
		/// Normal maps are supported only when FFT normal maps are not generated.
		/// </summary>
		Auto,

		/// <summary>
		/// Normal maps are always supported.
		/// </summary>
		Always
	}

	public enum WindWavesRenderMode
	{
		FullFFT,
		GerstnerAndFFTNormals,
		Gerstner,
		Disabled
	}
}
