﻿using UnityEngine;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PlayWay.Water
{
	public class WaterProfile : ScriptableObject
	{
		[SerializeField]
		private WaterSpectrumType spectrumType = WaterSpectrumType.Unified;

		[SerializeField]
		private float windSpeed = 22.0f;

		[Tooltip("Tile size in world units of all water maps including heightmap. High values lower overall quality, but low values make the water pattern noticeable.")]
		[SerializeField]
		private float tileSize = 180.0f;

		[SerializeField]
		private float tileScale = 1.0f;

		[Tooltip("Setting it to something else than 1.0 will make the spectrum less physically correct, but still may be useful at times.")]
		[SerializeField]
		private float wavesAmplitude = 1.0f;

		[SerializeField]
		private float wavesFrequencyScale = 1.0f;

		[Range(0.0f, 4.0f)]
		[SerializeField]
		private float horizontalDisplacementScale = 1.0f;

		[SerializeField]
		private float phillipsCutoffFactor = 2000.0f;

		[SerializeField]
		private float gravity = -9.81f;

		[Tooltip("It is the length of water in meters over which a wind has blown. Usually a distance to the closest land in the direction opposite to the wind.")]
		[SerializeField]
		private float fetch = 100000.0f;

		[Tooltip("Eliminates waves moving against the wind.")]
		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float directionality = 0.0f;

		[ColorUsage(false, true, 0.0f, 10.0f, 0.0f, 10.0f)]
		[SerializeField]
		private Color absorptionColor = new Color(0.35f, 0.04f, 0.001f, 1.0f);

		[SerializeField]
		private bool customUnderwaterAbsorptionColor = true;
		
		[Tooltip("Absorption color used by the underwater camera image-effect. Gradient describes color at each depth starting with 0 and ending on 600 units.")]
		[SerializeField]
		private Gradient absorptionColorByDepth;

		[SerializeField]
		private Gradient absorptionColorByDepthFlatGradient;

		[ColorUsage(false, true, 0.0f, 10.0f, 0.0f, 10.0f)]
		[SerializeField]
		private Color diffuseColor = new Color(0.1176f, 0.2196f, 0.2666f);

		[ColorUsage(false)]
		[SerializeField]
		private Color specularColor = new Color(0.0353f, 0.0471f, 0.0549f);

		[ColorUsage(false)]
		[SerializeField]
		private Color depthColor = new Color(0.0f, 0.0f, 0.0f);

		[ColorUsage(false)]
		[SerializeField]
		private Color emissionColor = new Color(0.0f, 0.0f, 0.0f);

		[ColorUsage(false)]
		[SerializeField]
		private Color reflectionColor = new Color(1.0f, 1.0f, 1.0f);
		
		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float smoothness = 0.94f;

		[SerializeField]
		private bool customAmbientSmoothness = false;

		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float ambientSmoothness = 0.94f;

		[FormerlySerializedAs("subsurfaceScatteringIntensity")]
		[Range(0.0f, 6.0f)]
		[SerializeField]
		private float isotropicScatteringIntensity = 1.0f;

		[Range(0.0f, 6.0f)]
		[SerializeField]
		private float forwardScatteringIntensity = 1.0f;

		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float subsurfaceScatteringContrast = 0.0f;
		
		[ColorUsage(false, true, 1.0f, 8.0f, 1.0f, 8.0f)]
		[SerializeField]
		private Color subsurfaceScatteringShoreColor = new Color(1.4f, 3.0f, 3.0f);

		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float refractionDistortion = 0.55f;
		
		[SerializeField]
		private float fresnelBias = 0.02040781f;

		[Range(0.5f, 20.0f)]
		[SerializeField]
		private float detailFadeDistance = 4.5f;

		[Range(0.1f, 10.0f)]
		[SerializeField]
		private float displacementNormalsIntensity = 2.0f;

		[Tooltip("Planar reflections are very good solution for calm weather, but you should fade them out for profiles with big waves (storms etc.) as they get completely incorrect then.")]
		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float planarReflectionIntensity = 0.6f;

		[Range(1.0f, 10.0f)]
		[SerializeField]
		private float planarReflectionFlatten = 6.0f;

		[Tooltip("Fixes some artifacts produced by planar reflections at grazing angles.")]
		[Range(0.0f, 0.008f)]
		[SerializeField]
		private float planarReflectionVerticalOffset = 0.0015f;

		[SerializeField]
		private float edgeBlendFactor = 0.15f;

		[SerializeField]
		private float directionalWrapSSS = 0.2f;

		[SerializeField]
		private float pointWrapSSS = 0.5f;

		[Tooltip("Used by the physics.")]
		[SerializeField]
		private float density = 998.6f;
		
		[Range(0.0f, 0.03f)]
		[SerializeField]
		private float underwaterBlurSize = 0.003f;

		[Range(0.0f, 2.0f)]
		[SerializeField]
		private float underwaterLightFadeScale = 0.8f;

		[Range(0.0f, 0.4f)]
		[SerializeField]
		private float underwaterDistortionsIntensity = 0.05f;

		[Range(0.02f, 0.5f)]
		[SerializeField]
		private float underwaterDistortionAnimationSpeed = 0.1f;

		[Range(1.0f, 64.0f)]
		[SerializeField]
		private float dynamicSmoothnessIntensity = 1.0f;

		[SerializeField]
		private NormalMapAnimation normalMapAnimation1 = new NormalMapAnimation(1.0f, -10.0f, 1.0f, new Vector2(1.0f, 1.0f));

		[SerializeField]
		private NormalMapAnimation normalMapAnimation2 = new NormalMapAnimation(-0.55f, 20.0f, 0.74f, new Vector2(1.5f, 1.5f));

		[SerializeField]
		private Texture2D normalMap;
		
		[SerializeField]
		private float foamIntensity = 1.0f;

		[SerializeField]
		private float foamThreshold = 1.0f;

		[Tooltip("Determines how fast foam will fade out.")]
		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float foamFadingFactor = 0.85f;

		[Range(0.0f, 5.0f)]
		[SerializeField]
		private float foamShoreIntensity = 1.0f;

		[Range(0.0f, 5.0f)]
		[SerializeField]
		private float foamShoreExtent = 1.0f;

		[SerializeField]
		private float foamNormalScale = 2.2f;

		[ColorUsage(false)]
		[SerializeField]
		private Color foamDiffuseColor = new Color(0.8f, 0.8f, 0.8f);

		[Tooltip("Alpha component is PBR smoothness.")]
		[SerializeField]
		private Color foamSpecularColor = new Color(1.0f, 1.0f, 1.0f, 0.0f);

		[Range(0.0f, 4.0f)]
		[SerializeField]
		private float sprayThreshold = 1.0f;

		[Range(0.0f, 0.999f)]
		[SerializeField]
		private float spraySkipRatio = 0.9f;

		[Range(0.25f, 4.0f)]
		[SerializeField]
		private float spraySize = 1.0f;

		[SerializeField]
		private Texture2D foamDiffuseMap;

		[SerializeField]
		private Texture2D foamNormalMap;

		[SerializeField]
		private Vector2 foamTiling = new Vector2(5.4f, 5.4f);
		
		private WaterWavesSpectrum spectrum;

		public WaterSpectrumType SpectrumType
		{
			get { return spectrumType; }
		}

		public WaterWavesSpectrum Spectrum
		{
			get
			{
				if(spectrum == null)
					CreateSpectrum();

                return spectrum;
			}
		}

		public float WindSpeed
		{
			get { return windSpeed; }
		}

		public float TileSize
		{
			get { return tileSize; }
		}

		public float TileScale
		{
			get { return tileScale; }
		}

		public float HorizontalDisplacementScale
		{
			get { return horizontalDisplacementScale; }
		}

		public float Gravity
		{
			get { return gravity; }
		}

		public float Directionality
		{
			get { return directionality; }
		}

		public Color AbsorptionColor
		{
			get { return absorptionColor; }
		}
		
		public Color DiffuseColor
		{
			get { return diffuseColor; }
		}

		public Color SpecularColor
		{
			get { return specularColor; }
		}

		public Color DepthColor
		{
			get { return depthColor; }
		}

		public Color EmissionColor
		{
			get { return emissionColor; }
		}

		public Color ReflectionColor
		{
			get { return reflectionColor; }
		}

		public float Smoothness
		{
			get { return smoothness; }
		}

		public bool CustomAmbientSmoothness
		{
			get { return customAmbientSmoothness; }
		}

		public float AmbientSmoothness
		{
			get { return customAmbientSmoothness ? ambientSmoothness : smoothness; }
		}

		public float IsotropicScatteringIntensity
		{
			get { return isotropicScatteringIntensity; }
		}

		public float ForwardScatteringIntensity
		{
			get { return forwardScatteringIntensity; }
		}

		public float SubsurfaceScatteringContrast
		{
			get { return subsurfaceScatteringContrast; }
		}

		public Color SubsurfaceScatteringShoreColor
		{
			get { return subsurfaceScatteringShoreColor; }
		}

		public float RefractionDistortion
		{
			get { return refractionDistortion; }
		}

		public float FresnelBias
		{
			get { return fresnelBias; }
		}
		
		public float DetailFadeDistance
		{
			get { return detailFadeDistance * detailFadeDistance; }
		}

		public float DisplacementNormalsIntensity
		{
			get { return displacementNormalsIntensity; }
		}

		public float PlanarReflectionIntensity
		{
			get { return planarReflectionIntensity; }
		}

		public float PlanarReflectionFlatten
		{
			get { return planarReflectionFlatten; }
		}

		public float PlanarReflectionVerticalOffset
		{
			get { return planarReflectionVerticalOffset; }
		}

		public float EdgeBlendFactor
		{
			get { return edgeBlendFactor; }
		}

		public float DirectionalWrapSSS
		{
			get { return directionalWrapSSS; }
		}

		public float PointWrapSSS
		{
			get { return pointWrapSSS; }
		}

		public float DynamicSmoothnessIntensity
		{
			get { return dynamicSmoothnessIntensity; }
		}

		public float Density
		{
			get { return density; }
		}

		public float UnderwaterBlurSize
		{
			get { return underwaterBlurSize; }
		}

		public float UnderwaterLightFadeScale
		{
			get { return underwaterLightFadeScale; }
		}

		public float UnderwaterDistortionsIntensity
		{
			get { return underwaterDistortionsIntensity; }
		}

		public float UnderwaterDistortionAnimationSpeed
		{
			get { return underwaterDistortionAnimationSpeed; }
		}

		public NormalMapAnimation NormalMapAnimation1
		{
			get { return normalMapAnimation1; }
		}

		public NormalMapAnimation NormalMapAnimation2
		{
			get { return normalMapAnimation2; }
		}

		public float FoamIntensity
		{
			get { return foamIntensity; }
		}

		public float FoamThreshold
		{
			get { return foamThreshold; }
		}

		public float FoamFadingFactor
		{
			get { return foamFadingFactor; }
		}

		public float FoamShoreIntensity
		{
			get { return foamShoreIntensity; }
		}

		public float FoamShoreExtent
		{
			get { return foamShoreExtent; }
		}

		public float FoamNormalScale
		{
			get { return foamNormalScale; }
		}

		public Color FoamDiffuseColor
		{
			get { return foamDiffuseColor; }
		}

		public Color FoamSpecularColor
		{
			get { return foamSpecularColor; }
		}

		public float SprayThreshold
		{
			get { return sprayThreshold; }
		}

		public float SpraySkipRatio
		{
			get { return spraySkipRatio; }
		}

		public float SpraySize
		{
			get { return spraySize; }
		}

		public Texture2D NormalMap
		{
			get { return normalMap; }
		}

		public Texture2D FoamDiffuseMap
		{
			get { return foamDiffuseMap; }
		}

		public Texture2D FoamNormalMap
		{
			get { return foamNormalMap; }
		}

		public Vector2 FoamTiling
		{
			get { return foamTiling; }
		}

		public float WavesFrequencyScale
		{
			get { return wavesFrequencyScale; }
		}

		public Gradient AbsorptionColorByDepth
		{
			get { return customUnderwaterAbsorptionColor ? absorptionColorByDepth : absorptionColorByDepthFlatGradient; }
		}

		public void CacheSpectrum()
		{
			if(spectrum == null)
				CreateSpectrum();
		}
		
		private void CreateSpectrum()
		{
			switch(spectrumType)
			{
				case WaterSpectrumType.Unified:
				{
					spectrum = new UnifiedSpectrum(tileSize, -gravity, windSpeed, wavesAmplitude, wavesFrequencyScale, fetch);
					break;
				}

				case WaterSpectrumType.Phillips:
				{
					spectrum = new PhillipsSpectrum(tileSize, -gravity, windSpeed, wavesAmplitude, phillipsCutoffFactor);
					break;
				}
			}
		}

#if UNITY_EDITOR
		[MenuItem("Assets/Create/PlayWay Water Profile")]
		public static void CreateProfile()
		{
			string path = AssetDatabase.GetAssetPath(Selection.activeObject);

			if(string.IsNullOrEmpty(path))
				path = "Assets";
			else if(System.IO.Path.GetExtension(path) != "")
				path = path.Replace(System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");

			var bundle = CreateInstance<WaterProfile>();
			AssetDatabase.CreateAsset(bundle, AssetDatabase.GenerateUniqueAssetPath(path + "/New Water Profile.asset"));
			AssetDatabase.SaveAssets();

			Selection.activeObject = bundle;
		}
#endif

		public enum WaterSpectrumType
		{
			Phillips,
			Unified
		}
    }
}
