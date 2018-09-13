using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Events;

namespace PlayWay.Water
{
	/// <summary>
	///     Renders wind waves on water surface and also resolves them on CPU for physics etc.
	/// </summary>
	public sealed class WindWaves
	{
		[Serializable]
		public sealed class Data
		{
			public Transform WindDirectionPointer;

			[Tooltip("Higher values increase quality, but also decrease performance. Directly controls quality of waves, foam and spray.")] [SerializeField]
			public int Resolution = 256;

			[Tooltip("Determines if 32-bit precision buffers should be used for computations (Default: off). Not supported on most mobile devices. This setting has impact on performance, even on PCs.\n\nTips:\n 1024 and higher - The difference is clearly visible, use this option on higher quality settings.\n 512 or lower - Keep it disabled.")] [SerializeField]
			public bool HighPrecision = true;

			[HideInInspector]
			[Obsolete("Unused as of 2.0b5.")]
			[Tooltip("Determines how small waves should be considered by the CPU in ongoing computations. Higher values will increase the precision of all wave computations done on CPU (GetHeightAt etc.), but may decrease performance. Most waves in the ocean spectrum have negligible visual impact and may be safely ignored.")] [SerializeField]
			public float CpuWaveThreshold = 0.008f;

			[HideInInspector]
			[Obsolete("Unused as of 2.0b5.")]
			[Tooltip("How many waves at most should be considered by the CPU.")]
			public int CpuMaxWaves = 2500;

			[HideInInspector]
			[Obsolete("Unused as of 2.0b5.")]
			[Tooltip("Determines final CPU FFT resolution (0 - acceptable, 1 - good, 2 - perfect, 3+ - insane).")]
			[Range(0, 4)]
			public int CpuFFTPrecisionBoost = 1;

			[Tooltip("What error in world units is acceptable for elevation sampling used by physics and custom scripts? Lower values mean better precision, but higher CPU usage.")]
			public float CpuDesiredStandardError = 0.12f;

			[Tooltip("Copying wave spectrum from other fluid will make this instance a lot faster.")]
			public Water CopyFrom;

			[Tooltip("Setting this property to any value greater than zero will loop the water spectra in that time. A good value is 10 seconds. Set to 0 to resolve sea state at each frame without any looping (best quality).")]
			public float LoopDuration = 0.0f;

			public WindWavesEvent WindDirectionChanged;
			public WindWavesEvent ResolutionChanged;
			public float MipBias = 0.0f;

			public WavesRendererFFT.Data WavesRendererFFTData;
			public WavesRendererGerstner.Data WavesRendererGerstnerData;
		}

		private readonly Water water;
		private readonly Data data;

		// I didn't found any practical reason for now to adjust these scales in inspector
		private Vector4 tileSizeScales = new Vector4(0.79241f, 0.163151f, 3.175131f, 13.7315131f);

		private int finalResolution;
		private bool finalHighPrecision;
		private float windSpeedMagnitude;
		private float spectrumDirectionality;
		private float tileSize;
		private float lastTileSize = float.NaN;
		private float lastUniformWaterScale = float.NaN;
		private Vector4 tileSizes;
		private Vector4 tileSizesInv;
		private Vector4 unscaledTileSizes;
		private Vector2 windDirection;
		private Vector2 windSpeed;
		private WaveSpectrumRenderMode finalRenderMode;
		private SpectrumResolver spectrumResolver;
		private Water runtimeCopyFrom;
		private bool isClone;
		private bool windSpeedChanged;
		private bool hasWindDirectionPointer;

		private WavesRendererFFT waterWavesFFT;
		private WavesRendererGerstner waterWavesGerstner;
		private DynamicSmoothness dynamicSmoothness;

		// cached shader ids
		private int tileSizeId;
		private int tileSizeInvId;
		private int tileSizeScalesId;
		private int maxDisplacementId;

		public WindWaves(Water water, Data data)
		{
			this.water = water;
			this.data = data;

			runtimeCopyFrom = data.CopyFrom;
			isClone = runtimeCopyFrom != null;

			tileSizeId = Shader.PropertyToID("_WaterTileSize");
			tileSizeInvId = Shader.PropertyToID("_WaterTileSizeInv");
			tileSizeScalesId = Shader.PropertyToID("_WaterTileSizeScales");
			maxDisplacementId = Shader.PropertyToID("_MaxDisplacement");

			CheckSupport();

			Validate();

			var spectrumShader = Shader.Find("PlayWay Water/Spectrum/Water Spectrum");

			if(spectrumResolver == null) spectrumResolver = new SpectrumResolver(water, this, spectrumShader);
			if(data.WindDirectionChanged == null) data.WindDirectionChanged = new WindWavesEvent();

			CreateObjects();
			
			ResolveFinalSettings(WaterQualitySettings.Instance.CurrentQualityLevel);

			if(!Application.isPlaying)
				return;

			water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
			OnProfilesChanged(water);
		}

		internal void Enable()
		{
			UpdateWind();

			ResolveFinalSettings(WaterQualitySettings.Instance.CurrentQualityLevel);
		}

		internal void Disable()
		{
			if(waterWavesFFT != null) waterWavesFFT.Disable();
			if(waterWavesGerstner != null) waterWavesGerstner.Disable();
			if(dynamicSmoothness != null) dynamicSmoothness.FreeResources();
		}
		
		/// <summary>
		/// Copying wave spectrum from other fluid will make this instance a lot faster.
		/// </summary>
		public Water CopyFrom
		{
			get { return runtimeCopyFrom; }
			set
			{
				if(data.CopyFrom != value || runtimeCopyFrom != value)
				{
					data.CopyFrom = value;
					runtimeCopyFrom = value;
					isClone = value != null;
					
					dynamicSmoothness.OnCopyModeChanged();
					waterWavesFFT.OnCopyModeChanged();
				}
			}
		}
		
		public SpectrumResolver SpectrumResolver
		{
			get { return data.CopyFrom == null ? spectrumResolver : data.CopyFrom.WindWaves.spectrumResolver; }
		}

		public WavesRendererFFT WaterWavesFFT
		{
			get { return waterWavesFFT; }
		}

		public WavesRendererGerstner WaterWavesGerstner
		{
			get { return waterWavesGerstner; }
		}
		
		public DynamicSmoothness DynamicSmoothness
		{
			get { return dynamicSmoothness; }
		}
		
		public WaveSpectrumRenderMode FinalRenderMode
		{
			get { return finalRenderMode; }
		}

		public Vector4 TileSizes
		{
			get { return tileSizes; }
		}

		public Vector4 TileSizesInv
		{
			get { return tileSizesInv; }
		}

		public Vector4 UnscaledTileSizes
		{
			get { return unscaledTileSizes; }
		}

		/// <summary>
		/// Current wind speed as resolved from the currently set profiles.
		/// </summary>
		public Vector2 WindSpeed
		{
			get { return windSpeed; }
		}

		public bool WindSpeedChanged
		{
			get { return windSpeedChanged; }
		}

		/// <summary>
		/// Current wind direction. It's controlled by the WindDirectionPointer.
		/// </summary>
		public Vector2 WindDirection
		{
			get { return windDirection; }
		}

		public Transform WindDirectionPointer
		{
			get { return data.WindDirectionPointer; }
		}

		/// <summary>
		/// Event invoked when wind direction changes.
		/// </summary>
		public WindWavesEvent WindDirectionChanged
		{
			get { return data.WindDirectionChanged; }
		}

		/// <summary>
		/// Event invoked when wind spectrum resolution changes.
		/// </summary>
		public WindWavesEvent ResolutionChanged
		{
			get { return data.ResolutionChanged ?? (data.ResolutionChanged = new WindWavesEvent()); }
		}

		public int Resolution
		{
			get { return data.Resolution; }
			set
			{
				if(data.Resolution == value)
					return;

				data.Resolution = value;
				ResolveFinalSettings(WaterQualitySettings.Instance.CurrentQualityLevel);
			}
		}

		public int FinalResolution
		{
			get { return finalResolution; }
		}

		public bool FinalHighPrecision
		{
			get { return finalHighPrecision; }
		}

		public bool HighPrecision
		{
			get { return data.HighPrecision; }
		}

		public float CpuDesiredStandardError
		{
			get { return data.CpuDesiredStandardError; }
		}

		public float LoopDuration
		{
			get { return data.LoopDuration; }
		}

		public Vector4 TileSizeScales
		{
			get { return tileSizeScales; }
		}

		public float MaxVerticalDisplacement
		{
			get { return spectrumResolver.MaxVerticalDisplacement; }
		}

		public float MaxHorizontalDisplacement
		{
			get { return spectrumResolver.MaxHorizontalDisplacement; }
		}

		public float SpectrumDirectionality
		{
			get { return spectrumDirectionality; }
		}

		internal void Validate()
		{
			if(Application.isPlaying)
				CopyFrom = data.CopyFrom;

#if UNITY_EDITOR
			if (data.CopyFrom != null && !Application.isPlaying)
			{
				data.CopyFrom.ForceStartup();

				var copiedWindWaves = data.CopyFrom.WindWaves;

				Assert.IsNotNull(copiedWindWaves);

				finalRenderMode = copiedWindWaves.finalRenderMode;
				data.Resolution = copiedWindWaves.data.Resolution;
				data.HighPrecision = copiedWindWaves.data.HighPrecision;
				data.CpuDesiredStandardError = copiedWindWaves.data.CpuDesiredStandardError;
			}
#endif

			if (data.CpuDesiredStandardError < 0.00001f)
				data.CpuDesiredStandardError = 0.00001f;

			hasWindDirectionPointer = (data.WindDirectionPointer != null);

			if(spectrumResolver != null)
			{
				ResolveFinalSettings(WaterQualitySettings.Instance.CurrentQualityLevel);

				waterWavesFFT.Validate();
				waterWavesGerstner.OnValidate(this);
			}

			if(water != null && tileSize != 0.0f)
				UpdateShaderParams();

			if(waterWavesFFT != null && waterWavesFFT.NormalMaps != null && waterWavesFFT.NormalMaps.Length != 0)
			{
				waterWavesFFT.GetNormalMap(0).mipMapBias = data.MipBias;
				waterWavesFFT.GetNormalMap(1).mipMapBias = data.MipBias;
			}
		}

		internal void Update()
		{
			UpdateWind();

			if(isClone)
			{
				tileSize = runtimeCopyFrom.WindWaves.tileSize;
				UpdateShaderParams();
				return;
			}

			if(!Application.isPlaying)
				return;

			spectrumResolver.Update();
			dynamicSmoothness.Update();
			UpdateShaderParams();
		}

		/// <summary>
		/// Resolves final component settings based on the desired values, quality settings and hardware limitations.
		/// </summary>
		internal void ResolveFinalSettings(WaterQualityLevel quality)
		{
			CreateObjects();

			var wavesMode = quality.wavesMode;

			if (wavesMode == WaterWavesMode.DisallowAll)
			{
				waterWavesFFT.Disable();
				waterWavesGerstner.Disable();
				return;
			}

			bool supportsFloats = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) || SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);

			int finalResolution = Mathf.Min(data.Resolution, quality.maxSpectrumResolution, SystemInfo.maxTextureSize);
			bool finalHighPrecision = data.HighPrecision && quality.allowHighPrecisionTextures && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat);

			var windWavesMode = water.ShaderSet.WindWavesMode;

			if(windWavesMode == WindWavesRenderMode.FullFFT && wavesMode == WaterWavesMode.AllowAll && supportsFloats)
				finalRenderMode = WaveSpectrumRenderMode.FullFFT;
			else if(windWavesMode <= WindWavesRenderMode.GerstnerAndFFTNormals && wavesMode <= WaterWavesMode.AllowNormalFFT && supportsFloats)
				finalRenderMode = WaveSpectrumRenderMode.GerstnerAndFFTNormals;
			else
				finalRenderMode = WaveSpectrumRenderMode.Gerstner;

			if(this.finalResolution != finalResolution)
			{
				lock (this)
				{
					this.finalResolution = finalResolution;
					this.finalHighPrecision = finalHighPrecision;

					if(spectrumResolver != null)
						spectrumResolver.OnMapsFormatChanged(true);

					if(ResolutionChanged != null)
						ResolutionChanged.Invoke(this);
				}
			}
			else if(this.finalHighPrecision != finalHighPrecision)
			{
				lock (this)
				{
					this.finalHighPrecision = finalHighPrecision;

					if(spectrumResolver != null)
						spectrumResolver.OnMapsFormatChanged(false);
				}
			}
			
			switch(finalRenderMode)
			{
				case WaveSpectrumRenderMode.FullFFT:
				{
					waterWavesFFT.RenderedMaps = WavesRendererFFT.MapType.Displacement | WavesRendererFFT.MapType.Normal;
					waterWavesFFT.Enable();

					waterWavesGerstner.Disable();
					break;
				}

				case WaveSpectrumRenderMode.GerstnerAndFFTNormals:
				{
					waterWavesFFT.RenderedMaps = WavesRendererFFT.MapType.Normal;
					waterWavesFFT.Enable();

					waterWavesGerstner.Enable();
					break;
				}

				case WaveSpectrumRenderMode.Gerstner:
				{
					waterWavesFFT.Disable();
                    waterWavesGerstner.Enable();
					break;
				}
			}
		}

		private void UpdateShaderParams()
		{
			float uniformWaterScale = water.UniformWaterScale;

			if(lastTileSize == tileSize && lastUniformWaterScale == uniformWaterScale)
				return;

			var block = water.Renderer.PropertyBlock;
			
			float scaledTileSize = tileSize * uniformWaterScale;
			tileSizes.x = scaledTileSize * tileSizeScales.x;
			tileSizes.y = scaledTileSize * tileSizeScales.y;
			tileSizes.z = scaledTileSize * tileSizeScales.z;
			tileSizes.w = scaledTileSize * tileSizeScales.w;
			block.SetVector(tileSizeId, tileSizes);						// _WaterTileSize

			tileSizesInv.x = 1.0f / tileSizes.x;
			tileSizesInv.y = 1.0f / tileSizes.y;
			tileSizesInv.z = 1.0f / tileSizes.z;
			tileSizesInv.w = 1.0f / tileSizes.w;
			block.SetVector(tileSizeInvId, tileSizesInv);                 // _WaterTileSizeInv

			lastUniformWaterScale = uniformWaterScale;
			lastTileSize = tileSize;
		}

		private void OnProfilesChanged(Water water)
		{
			tileSize = 0.0f;
			windSpeedMagnitude = 0.0f;
			spectrumDirectionality = 0.0f;

			var profiles = water.ProfilesManager.Profiles;

			for (int i = 0; i < profiles.Length; ++i)
			{
				var profile = profiles[i].Profile;
				float weight = profiles[i].Weight;

				tileSize += profile.TileSize*profile.TileScale*weight;
				windSpeedMagnitude += profile.WindSpeed*weight;
				spectrumDirectionality += profile.Directionality*weight;
			}

			// scale by quality settings
			var waterQualitySettings = WaterQualitySettings.Instance;
			tileSize *= waterQualitySettings.TileSizeScale;
			unscaledTileSizes = tileSize * tileSizeScales;
			UpdateShaderParams();

			var block = water.Renderer.PropertyBlock;
			block.SetVector(tileSizeScalesId, new Vector4(tileSizeScales.x / tileSizeScales.y, tileSizeScales.x / tileSizeScales.z, tileSizeScales.x / tileSizeScales.w, 0.0f));         // _WaterTileSizeScales

			spectrumResolver.OnProfilesChanged();

			block.SetFloat(maxDisplacementId, spectrumResolver.MaxHorizontalDisplacement);
		}

		internal void Destroy()
		{
			if(spectrumResolver != null)
			{
				spectrumResolver.OnDestroy();
				spectrumResolver = null;
			}

			if (waterWavesFFT != null) waterWavesFFT.OnDestroy();
		}

		private void UpdateWind()
		{
			Vector2 newWindDirection;
			
			if(hasWindDirectionPointer)				// used bool var to avoid calling Unity's overloaded == operator and gain some performance
			{
				Vector3 forward = data.WindDirectionPointer.forward;
				float len = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
				newWindDirection = new Vector2(forward.x / len, forward.z / len);
			}
			else
				newWindDirection = new Vector2(1.0f, 0.0f);

			Vector2 newWindSpeed = new Vector2(
				newWindDirection.x * windSpeedMagnitude,
				newWindDirection.y * windSpeedMagnitude
			);

			if(windSpeed.x != newWindSpeed.x || windSpeed.y != newWindSpeed.y)
			{
				windDirection = newWindDirection;
				windSpeed = newWindSpeed;
				windSpeedChanged = true;

				spectrumResolver.SetWindDirection(windDirection);
			}
			else
				windSpeedChanged = false;
        }

		private void CreateObjects()
		{
			if(waterWavesFFT == null) waterWavesFFT = new WavesRendererFFT(water, this, data.WavesRendererFFTData);
			if(waterWavesGerstner == null) waterWavesGerstner = new WavesRendererGerstner(water, this, data.WavesRendererGerstnerData);
			if(dynamicSmoothness == null) dynamicSmoothness = new DynamicSmoothness(water, this);
		}

		private void CheckSupport()
		{
			if(data.HighPrecision && (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat) || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat)))
				finalHighPrecision = false;

			if(!data.HighPrecision && (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf) || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)))
			{
				if(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat))
					finalHighPrecision = true;
				else if(water.ShaderSet.WindWavesMode == WindWavesRenderMode.FullFFT)
				{
#if UNITY_EDITOR
					Debug.LogError("Your hardware doesn't support floating point render textures. FFT water waves won't work in editor.");
#endif

					finalRenderMode = WaveSpectrumRenderMode.Gerstner;
				}
			}
		}

		internal void OnWaterRender(Camera camera)
		{
			if(!Application.isPlaying) return;

			if(waterWavesFFT.Enabled)
				waterWavesFFT.OnWaterRender(camera);

			if(waterWavesGerstner.Enabled)
				waterWavesGerstner.OnWaterRender(camera);
		}
		
		public Vector3 GetDisplacementAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			return spectrumResolver.GetDisplacementAt(x, z, spectrumStart, spectrumEnd, time);
		}

		public Vector2 GetHorizontalDisplacementAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			return spectrumResolver.GetHorizontalDisplacementAt(x, z, spectrumStart, spectrumEnd, time);
		}

		public float GetHeightAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			return spectrumResolver.GetHeightAt(x, z, spectrumStart, spectrumEnd, time);
		}

		public Vector4 GetForceAndHeightAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			return spectrumResolver.GetForceAndHeightAt(x, z, spectrumStart, spectrumEnd, time);
		}

		[Serializable]
		public class WindWavesEvent : UnityEvent<WindWaves> { };
	}

	public enum WaveSpectrumRenderMode
	{
		FullFFT,
		GerstnerAndFFTNormals,
		Gerstner
	}
}
