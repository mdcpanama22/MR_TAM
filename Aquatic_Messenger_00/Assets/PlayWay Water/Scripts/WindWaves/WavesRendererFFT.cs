using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Displays water spectrum using Fast Fourier Transform. Uses vertex shader texture fetch available on platforms with
	///     Shader Model 3.0+.
	/// </summary>
	public sealed class WavesRendererFFT
	{
		[System.Serializable]
		public sealed class Data
		{
			[Tooltip("Determines if GPU partial derivatives or Fast Fourier Transform (high quality) should be used to compute normal map (Recommended: on). Works only if displacement map rendering is enabled.")]
			public bool HighQualityNormalMaps = true;

#pragma warning disable 0414
			[Tooltip("Check this option, if your water is flat or game crashes instantly on a DX11 GPU (in editor or build). Compute shaders are very fast, so use this as a last resort.")]
			public bool ForcePixelShader = false;
#pragma warning restore 0414

			[Tooltip("Fixes crest artifacts during storms, but lowers overall quality. Enabled by default when used with additive water volumes as it is actually needed and disabled in all other cases.")]
			public FlattenMode FlattenMode = FlattenMode.Auto;

			[Tooltip("Sea state will be cached in the specified frame count for extra performance, if LoopLength on WindWaves is set to a value greater than zero.")]
			public int CachedFrameCount = 180;
		}

		public enum SpectrumType
		{
			Phillips,
			Unified
		}

		[System.Flags]
		public enum MapType
		{
			Displacement = 1,
			Normal = 2
		}

		public enum FlattenMode
		{
			Auto,
			ForcedOn,
			ForcedOff
		}
		
		private Shader fftShader;
		private Shader fftUtilitiesShader;

		private readonly Water water;
		private readonly WindWaves windWaves;
		private readonly Data data;

		private RenderTexture[][] normalMapsCache;
		private RenderTexture[][] displacementMapsCache;
		private bool[] isCachedFrameValid;

		private RenderTexture[] normalMaps;
		private RenderTexture[] displacementMaps;

		private RenderTexturesCache singleTargetCache;
		private RenderTexturesCache doubleTargetCache;
		
		private GpuFFT heightFFT;
		private GpuFFT normalFFT;
		private GpuFFT displacementFFT;
		private Material fftUtilitiesMaterial;
		private ComputeShader dx11FFT;

		private MapType renderedMaps;
		private bool finalHighQualityNormalMaps;
		private bool flatten;
		private bool copyModeDirty;
		private int waveMapsFrame;
		private Water lastCopyFrom;

		public event System.Action ResourcesChanged;

		private static int heightTexId;
		private static int displacementTexId;
		private static int horizontalDisplacementScaleId;
		private static int jacobianScaleId;
		private static int offsetId;
		
		private static readonly Vector4[] offsets = { new Vector4(0.0f, 0.0f, 0.0f, 0.0f), new Vector4(0.5f, 0.0f, 0.0f, 0.0f), new Vector4(0.0f, 0.5f, 0.0f, 0.0f), new Vector4(0.5f, 0.5f, 0.0f, 0.0f) };
		private static readonly Vector4[] offsetsDual = { new Vector4(0.0f, 0.0f, 0.5f, 0.0f), new Vector4(0.0f, 0.5f, 0.5f, 0.5f) };

		public WavesRendererFFT(Water water, WindWaves windWaves, Data data)
		{
			this.water = water;
			this.windWaves = windWaves;
			this.data = data;

			if (windWaves.LoopDuration != 0.0f)
			{
				normalMapsCache = new RenderTexture[data.CachedFrameCount][];
				displacementMapsCache = new RenderTexture[data.CachedFrameCount][];
				isCachedFrameValid = new bool[data.CachedFrameCount];

				water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
			}

			if(heightTexId == 0)
				CacheShaderIds();

			Validate();
		}

		internal void Enable()
		{
			if(Enabled) return;
			
			Enabled = true;

			OnCopyModeChanged();

			if(Application.isPlaying)
			{
				if(lastCopyFrom == null)
					ValidateResources();

				windWaves.ResolutionChanged.AddListener(OnResolutionChanged);
			}

			fftUtilitiesMaterial = new Material(fftUtilitiesShader) {hideFlags = HideFlags.DontSave};
		}

		internal void Disable()
		{
			if(!Enabled) return;

			Enabled = false;

			Dispose(false);
		}

		internal void OnDestroy()
		{
			Dispose(true);
		}

		public MapType RenderedMaps
		{
			get { return renderedMaps; }
			set
			{
				renderedMaps = value;

				if(Enabled && Application.isPlaying)
				{
					Dispose(false);
					ValidateResources();
				}
			}
		}

		public bool Enabled { get; private set; }
		
		public Texture GetDisplacementMap(int index)
		{
			return displacementMaps != null ? displacementMaps[index] : null;
		}

		public Texture GetNormalMap(int index)
		{
			return normalMaps[index];
		}
		
		public RenderTexture[] NormalMaps
		{
			get { return normalMaps; }
		}

		private void ValidateResources()
		{
			if(windWaves.CopyFrom == null)
			{
				ValidateFFT(ref heightFFT, (renderedMaps & MapType.Displacement) != 0, false);
				ValidateFFT(ref displacementFFT, (renderedMaps & MapType.Displacement) != 0, true);
				ValidateFFT(ref normalFFT, (renderedMaps & MapType.Normal) != 0, true);
			}

			if(displacementMaps == null || normalMaps == null)
			{
				bool flatten = (!water.Volume.Boundless && data.FlattenMode == FlattenMode.Auto) || data.FlattenMode == FlattenMode.ForcedOn;

				if(this.flatten != flatten)
					this.flatten = flatten;
				
				RenderTexture[] usedDisplacementMaps, usedNormalMaps;

				if(windWaves.CopyFrom == null)
				{
					int resolution = windWaves.FinalResolution;
					int packResolution = resolution << 1;
					singleTargetCache = RenderTexturesCache.GetCache(packResolution, packResolution, 0, RenderTextureFormat.RHalf, true, heightFFT is Dx11FFT);
					doubleTargetCache = RenderTexturesCache.GetCache(packResolution, packResolution, 0, RenderTextureFormat.RGHalf, true, displacementFFT is Dx11FFT);

					if(displacementMaps == null && (renderedMaps & MapType.Displacement) != 0)
						CreateRenderTextures(ref displacementMaps, "Water Displacement Map", RenderTextureFormat.ARGBHalf, 4, true);

					if(normalMaps == null && (renderedMaps & MapType.Normal) != 0)
						CreateRenderTextures(ref normalMaps, "Water Normal Map", RenderTextureFormat.ARGBHalf, 2, true);

					usedDisplacementMaps = displacementMaps;
					usedNormalMaps = normalMaps;
                }
				else
				{
					var copyFrom = windWaves.CopyFrom;

					if(copyFrom.WindWaves.WaterWavesFFT.windWaves == null)
						copyFrom.WindWaves.ResolveFinalSettings(WaterQualitySettings.Instance.CurrentQualityLevel);

					copyFrom.WindWaves.WaterWavesFFT.ValidateResources();
					
					usedDisplacementMaps = copyFrom.WindWaves.WaterWavesFFT.displacementMaps;
					usedNormalMaps = copyFrom.WindWaves.WaterWavesFFT.normalMaps;
                }

				for(int scaleIndex = 0; scaleIndex < 4; ++scaleIndex)
				{
					string suffix = scaleIndex != 0 ? scaleIndex.ToString() : "";

					if(usedDisplacementMaps != null)
					{
						string texName = "_GlobalDisplacementMap" + suffix;
						water.Renderer.PropertyBlock.SetTexture(texName, usedDisplacementMaps[scaleIndex]);
					}

					if(scaleIndex < 2 && usedNormalMaps != null)
					{
						string texName = "_GlobalNormalMap" + suffix;
						water.Renderer.PropertyBlock.SetTexture(texName, usedNormalMaps[scaleIndex]);
					}
				}

				if(ResourcesChanged != null)
					ResourcesChanged();
            }
		}

		public void OnCopyModeChanged()
		{
			copyModeDirty = true;

			if(lastCopyFrom != null)
				lastCopyFrom.WindWaves.WaterWavesFFT.ResourcesChanged -= ValidateResources;

			if(windWaves.CopyFrom != null)
				windWaves.CopyFrom.WindWaves.WaterWavesFFT.ResourcesChanged += ValidateResources;

			lastCopyFrom = windWaves.CopyFrom;

			Dispose(false);
		}

		// ReSharper disable once RedundantAssignment
		private void CreateRenderTextures(ref RenderTexture[] renderTextures, string name, RenderTextureFormat format, int count, bool mipMaps)
		{
			renderTextures = new RenderTexture[count];

			for(int i = 0; i < count; ++i)
				renderTextures[i] = CreateRenderTexture(name, format, mipMaps);
		}

		private RenderTexture CreateRenderTexture(string name, RenderTextureFormat format, bool mipMaps)
		{
			var texture = new RenderTexture(windWaves.FinalResolution, windWaves.FinalResolution, 0, format, RenderTextureReadWrite.Linear)
			{
				name = name,
				hideFlags = HideFlags.DontSave,
				wrapMode = TextureWrapMode.Repeat
			};

			if(mipMaps && WaterProjectSettings.Instance.AllowFloatingPointMipMaps)
			{
				texture.filterMode = FilterMode.Trilinear;
				texture.useMipMap = true;
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4
				texture.generateMips = true;
#else
				texture.autoGenerateMips = true;
#endif
			}
			else
				texture.filterMode = FilterMode.Bilinear;

			return texture;
		}

		private void ValidateFFT(ref GpuFFT fft, bool present, bool twoChannels)
		{
			if(present)
			{
				if(fft == null)
					fft = ChooseBestFFTAlgorithm(twoChannels);
			}
			else if(fft != null)
			{
				fft.Dispose();
				fft = null;
			}
		}

		private GpuFFT ChooseBestFFTAlgorithm(bool twoChannels)
		{
			GpuFFT fft;

			int resolution = windWaves.FinalResolution;

#if !UNITY_IOS && !UNITY_ANDROID && !UNITY_PS3 && !UNITY_PS4 && !UNITY_BLACKBERRY && !UNITY_TIZEN && !UNITY_WEBGL && !UNITY_STANDALONE_OSX && !UNITY_STANDALONE_LINUX && !UNITY_EDITOR_OSX
			if(!data.ForcePixelShader && dx11FFT != null && SystemInfo.supportsComputeShaders && /*resolution >= 128 && */resolution <= 512)
				fft = new Dx11FFT(dx11FFT, resolution, windWaves.FinalHighPrecision || resolution >= 2048, twoChannels);
			else
#endif
				fft = new PixelShaderFFT(fftShader, resolution, windWaves.FinalHighPrecision || resolution >= 2048, twoChannels);

			fft.SetupMaterials();

			return fft;
		}

		internal void ResolveFinalSettings(WaterQualityLevel qualityLevel)
		{
			finalHighQualityNormalMaps = data.HighQualityNormalMaps;

			if(!qualityLevel.allowHighQualityNormalMaps)
				finalHighQualityNormalMaps = false;

			if((renderedMaps & MapType.Displacement) == 0)           // if heightmap is not rendered, only high-quality normal map is possible
				finalHighQualityNormalMaps = true;
		}

		internal void Validate()
		{
			dx11FFT = water.ShaderSet.GetComputeShader("DX11 FFT");

			if(fftShader == null)
				fftShader = Shader.Find("PlayWay Water/Base/FFT");

			if(fftUtilitiesShader == null)
				fftUtilitiesShader = Shader.Find("PlayWay Water/Utilities/FFT Utilities");

			if(Application.isPlaying && Enabled)
				ResolveFinalSettings(WaterQualitySettings.Instance.CurrentQualityLevel);
		}

		private void OnProfilesChanged(Water water)
		{
			for(int i = isCachedFrameValid.Length - 1; i >= 0; --i)
				isCachedFrameValid[i] = false;
		}

		private void Dispose(bool total)
		{
			waveMapsFrame = -1;

			if(heightFFT != null)
			{
				heightFFT.Dispose();
				heightFFT = null;
			}

			if(normalFFT != null)
			{
				normalFFT.Dispose();
				normalFFT = null;
			}

			if(displacementFFT != null)
			{
				displacementFFT.Dispose();
				displacementFFT = null;
			}

			if(normalMaps != null)
			{
				foreach(var normalMap in normalMaps)
					normalMap.Destroy();

				normalMaps = null;
			}

			if(displacementMaps != null)
			{
				foreach(var displacementMap in displacementMaps)
					displacementMap.Destroy();

				displacementMaps = null;
			}

			if (normalMapsCache != null)
			{
				for (int i = normalMapsCache.Length - 1; i >= 0; --i)
				{
					var normalMapsCacheFrame = normalMapsCache[i];

					if (normalMapsCacheFrame != null)
					{
						for (int ii = normalMapsCacheFrame.Length - 1; ii >= 0; --ii)
							normalMapsCacheFrame[ii].Destroy();

						normalMapsCache[i] = null;
					}
				}
			}

			if (displacementMapsCache != null)
			{
				for (int i = displacementMapsCache.Length - 1; i >= 0; --i)
				{
					var displacementMapsCacheFrame = displacementMapsCache[i];

					if (displacementMapsCacheFrame != null)
					{
						for (int ii = displacementMapsCacheFrame.Length - 1; ii >= 0; --ii)
							displacementMapsCacheFrame[ii].Destroy();

						displacementMapsCache[i] = null;
					}
				}
			}

			if(total)
			{
				if(fftUtilitiesMaterial != null)
				{
					fftUtilitiesMaterial.Destroy();
					fftUtilitiesMaterial = null;
				}
			}
		}

		public void OnWaterRender(Camera camera)
		{
			if(fftUtilitiesMaterial == null) return;

			ValidateWaveMaps();
		}

		private void OnResolutionChanged(WindWaves windWaves)
		{
			Dispose(false);
			ValidateResources();
		}

		private void ValidateWaveMaps()
		{
			int frameCount = Time.frameCount;
			
			if(waveMapsFrame == frameCount || !Application.isPlaying)
				return;         // it's already done

			if(lastCopyFrom != null)
			{
				if(copyModeDirty)
				{
					copyModeDirty = false;
					ValidateResources();
				}

				return;
			}
			
			waveMapsFrame = frameCount;

			if (windWaves.LoopDuration == 0.0f)
			{
				RenderMaps(water.Time, displacementMaps, normalMaps);
			}
			else
			{
				RenderMapsFromCache(water.Time, displacementMaps, normalMaps);
			}
		}

		private void RenderMapsFromCache(float time, RenderTexture[] displacementMaps, RenderTexture[] normalMaps)
		{
			float frameIndexFloat = data.CachedFrameCount*FastMath.FracAdditive(time/windWaves.LoopDuration);
			int frameIndex = (int)frameIndexFloat;
			float blendFactor = frameIndexFloat - frameIndex;

			RenderTexture[] displacementMaps1;
			RenderTexture[] normalMaps1;
			RetrieveCachedFrame(frameIndex, out displacementMaps1, out normalMaps1);

			int nextFrameIndex = frameIndex + 1;

			if (nextFrameIndex >= data.CachedFrameCount)
				nextFrameIndex = 0;

			RenderTexture[] displacementMaps2;
			RenderTexture[] normalMaps2;
			RetrieveCachedFrame(nextFrameIndex, out displacementMaps2, out normalMaps2);

			fftUtilitiesMaterial.SetFloat("_BlendFactor", blendFactor);

			for (int scaleIndex = 0; scaleIndex < 4; ++scaleIndex)
			{
				fftUtilitiesMaterial.SetTexture("_Texture1", displacementMaps1[scaleIndex]);
				fftUtilitiesMaterial.SetTexture("_Texture2", displacementMaps2[scaleIndex]);
				Graphics.Blit(null, displacementMaps[scaleIndex], fftUtilitiesMaterial, 6);
			}

			for (int scalesIndex = 0; scalesIndex < 2; ++scalesIndex)
			{
				fftUtilitiesMaterial.SetTexture("_Texture1", normalMaps1[scalesIndex]);
				fftUtilitiesMaterial.SetTexture("_Texture2", normalMaps2[scalesIndex]);
				Graphics.Blit(null, normalMaps[scalesIndex], fftUtilitiesMaterial, 6);
			}
		}

		private void RetrieveCachedFrame(int frameIndex, out RenderTexture[] displacementMaps, out RenderTexture[] normalMaps)
		{
			//int frameCount = data.CachedFrameCount*windWaves.TileSizeScales[scaleIndex];
			float frameTime = (float)frameIndex / data.CachedFrameCount * windWaves.LoopDuration;

			if (!isCachedFrameValid[frameIndex])
			{
				isCachedFrameValid[frameIndex] = true;

				if((renderedMaps & MapType.Displacement) != 0 && displacementMapsCache[frameIndex] == null)
					CreateRenderTextures(ref displacementMapsCache[frameIndex], "Water Displacement Map", RenderTextureFormat.ARGBHalf, 4, true);

				if((renderedMaps & MapType.Normal) != 0 && normalMapsCache[frameIndex] == null)
					CreateRenderTextures(ref normalMapsCache[frameIndex], "Water Normal Map", RenderTextureFormat.ARGBHalf, 2, true);

				RenderMaps(frameTime, displacementMapsCache[frameIndex], normalMapsCache[frameIndex]);
			}

			displacementMaps = displacementMapsCache[frameIndex];
			normalMaps = normalMapsCache[frameIndex];
		}

		private void RenderMaps(float time, RenderTexture[] displacementMaps, RenderTexture[] normalMaps)
		{
			// render needed spectra
			Texture heightSpectrum, normalSpectrum, displacementSpectrum;
			RenderSpectra(time, out heightSpectrum, out normalSpectrum, out displacementSpectrum);

			// displacement
			if((renderedMaps & MapType.Displacement) != 0)
			{
				TemporaryRenderTexture packedHeightMaps = singleTargetCache.GetTemporary();
				TemporaryRenderTexture packedHorizontalDisplacementMaps = doubleTargetCache.GetTemporary();

				heightFFT.ComputeFFT(heightSpectrum, packedHeightMaps);
				displacementFFT.ComputeFFT(displacementSpectrum, packedHorizontalDisplacementMaps);

				fftUtilitiesMaterial.SetTexture(heightTexId, packedHeightMaps);
				fftUtilitiesMaterial.SetTexture(displacementTexId, packedHorizontalDisplacementMaps);
				fftUtilitiesMaterial.SetFloat(horizontalDisplacementScaleId, water.Materials.HorizontalDisplacementScale);

				for(int scaleIndex = 0; scaleIndex < 4; ++scaleIndex)
				{
					fftUtilitiesMaterial.SetFloat(jacobianScaleId, water.Materials.HorizontalDisplacementScale * 0.1f * displacementMaps[scaleIndex].width / windWaves.TileSizes[scaleIndex]);     // * 220.0f * displacementMaps[scaleIndex].width / (2048.0f * water.SpectraRenderer.TileSizes[scaleIndex])
					fftUtilitiesMaterial.SetVector(offsetId, offsets[scaleIndex]);

					Graphics.Blit(null, displacementMaps[scaleIndex], fftUtilitiesMaterial, 1);
				}

				packedHeightMaps.Dispose();
				packedHorizontalDisplacementMaps.Dispose();
			}

			// normals
			if((renderedMaps & MapType.Normal) != 0)
			{
				if(!finalHighQualityNormalMaps)
				{
					for(int scalesIndex = 0; scalesIndex < 2; ++scalesIndex)
					{
						int resolution = windWaves.FinalResolution;

						fftUtilitiesMaterial.SetFloat("_Intensity1", 0.58f * resolution / windWaves.TileSizes[scalesIndex * 2]);
						fftUtilitiesMaterial.SetFloat("_Intensity2", 0.58f * resolution / windWaves.TileSizes[scalesIndex * 2 + 1]);
						fftUtilitiesMaterial.SetTexture("_MainTex", displacementMaps[scalesIndex << 1]);
						fftUtilitiesMaterial.SetTexture("_SecondTex", displacementMaps[(scalesIndex << 1) + 1]);
						fftUtilitiesMaterial.SetFloat("_MainTex_Texel_Size", 1.0f / displacementMaps[scalesIndex << 1].width);
						Graphics.Blit(null, normalMaps[scalesIndex], fftUtilitiesMaterial, 0);
					}
				}
				else
				{
					TemporaryRenderTexture packedNormalMaps = doubleTargetCache.GetTemporary();
					normalFFT.ComputeFFT(normalSpectrum, packedNormalMaps);

					for(int scalesIndex = 0; scalesIndex < 2; ++scalesIndex)
					{
						fftUtilitiesMaterial.SetVector(offsetId, offsetsDual[scalesIndex]);
						Graphics.Blit(packedNormalMaps, normalMaps[scalesIndex], fftUtilitiesMaterial, 3);
					}

					packedNormalMaps.Dispose();
				}
			}
		}

		private void RenderSpectra(float time, out Texture heightSpectrum, out Texture normalSpectrum, out Texture displacementSpectrum)
		{
			if(renderedMaps == MapType.Normal)
			{
				heightSpectrum = null;
				displacementSpectrum = null;
				normalSpectrum = windWaves.SpectrumResolver.RenderNormalsSpectrumAt(time);
			}
			else if((renderedMaps & MapType.Normal) == 0 || !finalHighQualityNormalMaps)
			{
				normalSpectrum = null;
				windWaves.SpectrumResolver.RenderDisplacementsSpectraAt(time, out heightSpectrum, out displacementSpectrum);
			}
			else
				windWaves.SpectrumResolver.RenderCompleteSpectraAt(time, out heightSpectrum, out normalSpectrum, out displacementSpectrum);
		}

		private void CacheShaderIds()
		{
			heightTexId = Shader.PropertyToID("_HeightTex");
			displacementTexId = Shader.PropertyToID("_DisplacementTex");
			horizontalDisplacementScaleId = Shader.PropertyToID("_HorizontalDisplacementScale");
			jacobianScaleId = Shader.PropertyToID("_JacobianScale");
			offsetId = Shader.PropertyToID("_Offset");
		}
	}
}
