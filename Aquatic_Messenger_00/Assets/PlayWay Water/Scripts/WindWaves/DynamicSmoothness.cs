using UnityEngine;
using UnityEngine.Assertions;

namespace PlayWay.Water
{
	/// <summary>
	///     Precomputes spectra variance used by shader microfacet model. Currently works only on platforms with compute
	///     shaders. General SM 3.0 support will be added later.
	///     <seealso cref="WindWaves.DynamicSmoothness" />
	/// </summary>
	[System.Serializable]
	public class DynamicSmoothness
	{
		private readonly Water water;
		private readonly WindWaves windWaves;
		private readonly bool supported;

		// variance
		private ComputeShader varianceShader;
		private RenderTexture varianceTexture;
		private int lastResetIndex;
		private int currentIndex;
		private bool finished;
		private bool initialized;
		private float dynamicSmoothnessIntensity;
		
		public DynamicSmoothness(Water water, WindWaves windWaves)
		{
			this.water = water;
			this.windWaves = windWaves;
			this.supported = CheckSupport();

			varianceShader = water.ShaderSet.GetComputeShader("Spectral Variances");

			OnCopyModeChanged();
		}
		
		public bool Enabled
		{
			get { return water.ShaderSet.SmoothnessMode == DynamicSmoothnessMode.Physical /* && supported*/; }
		}

		public Texture VarianceTexture
		{
			get { return varianceTexture; }
		}

		/// <summary>
		/// You need to set this in your script, when instantiating WindWaves manually as compute shaders need to be directly referenced in Unity.
		/// </summary>
		public ComputeShader ComputeShader
		{
			get { return varianceShader; }
			set { varianceShader = value; }
		}

		public void FreeResources()
		{
			if(varianceTexture != null)
			{
				varianceTexture.Destroy();
				varianceTexture = null;
			}
		}

		public void OnCopyModeChanged()
		{
			if(windWaves != null && windWaves.CopyFrom != null)
			{
				windWaves.CopyFrom.ForceStartup();

				Assert.IsNotNull(windWaves.CopyFrom.WindWaves);

				FreeResources();
				
				var copyFromWindWaves = windWaves.CopyFrom.WindWaves;
				copyFromWindWaves.DynamicSmoothness.ValidateVarianceTextures(copyFromWindWaves);
				water.Renderer.PropertyBlock.SetTexture("_SlopeVariance", copyFromWindWaves.DynamicSmoothness.varianceTexture);
			}
		}

		public bool CheckSupport()
		{
			return SystemInfo.supportsComputeShaders && SystemInfo.supports3DTextures;
		}
		
		public void Update()
		{
			if(water.ShaderSet.SmoothnessMode != DynamicSmoothnessMode.Physical || !supported) return;

			if(!initialized) InitializeVariance();

			ValidateVarianceTextures(windWaves);

			if(!finished)
				RenderNextPixel();
		}

		private void InitializeVariance()
		{
			initialized = true;
			
			water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
			windWaves.WindDirectionChanged.AddListener(OnWindDirectionChanged);

			OnProfilesChanged(water);
		}

		private void ValidateVarianceTextures(WindWaves windWaves)
		{
			if (varianceTexture == null)
			{
				varianceTexture = CreateVarianceTexture(RenderTextureFormat.RGHalf);
				ResetComputations();
			}

			if(!varianceTexture.IsCreated())
			{
				varianceTexture.Create();
				water.Renderer.PropertyBlock.SetTexture("_SlopeVariance", varianceTexture);

				lastResetIndex = 0;
				currentIndex = 0;
			}
		}

		private void RenderNextPixel()
		{
			varianceShader.SetInt("_FFTSize", windWaves.FinalResolution);
			varianceShader.SetInt("_FFTSizeHalf", windWaves.FinalResolution >> 1);
			varianceShader.SetFloat("_VariancesSize", varianceTexture.width);
			varianceShader.SetFloat("_IntensityScale", dynamicSmoothnessIntensity);
			varianceShader.SetVector("_TileSizes", windWaves.TileSizes);
			varianceShader.SetVector("_Coordinates", new Vector4(currentIndex % 4, (currentIndex >> 2) % 4, currentIndex >> 4, 0));
			varianceShader.SetTexture(0, "_Spectrum", windWaves.SpectrumResolver.GetRawDirectionalSpectrum());
			varianceShader.SetTexture(0, "_Variance", varianceTexture);
			varianceShader.Dispatch(0, 1, 1, 1);

			++currentIndex;

			if(currentIndex >= 64)
				currentIndex = 0;

			if(currentIndex == lastResetIndex)
				finished = true;
        }
		
		private void ResetComputations()
		{
			lastResetIndex = currentIndex;
			finished = false;
		}

		private static RenderTexture CreateVarianceTexture(RenderTextureFormat format)
		{
			var variancesTexture = new RenderTexture(4, 4, 0, format, RenderTextureReadWrite.Linear)
			{
				hideFlags = HideFlags.DontSave,
				volumeDepth = 4,
				enableRandomWrite = true,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
#if UNITY_5_4
				dimension = UnityEngine.Rendering.TextureDimension.Tex3D
#else
				isVolume = true
#endif
			};

			return variancesTexture;
		}
		
		private void OnProfilesChanged(Water water)
		{
			ResetComputations();

			dynamicSmoothnessIntensity = 0.0f;

			var profiles = water.ProfilesManager.Profiles;

			for(int i= profiles.Length-1; i>=0; --i)
				dynamicSmoothnessIntensity += profiles[i].Profile.DynamicSmoothnessIntensity * profiles[i].Weight;
        }

		private void OnWindDirectionChanged(WindWaves windWaves)
		{
			ResetComputations();
		}
    }
}
