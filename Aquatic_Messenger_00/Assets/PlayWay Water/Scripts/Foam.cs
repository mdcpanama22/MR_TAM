using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	public class Foam
	{
		[System.Serializable]
		public class Data
		{
			[Tooltip("Foam map supersampling in relation to the waves simulator resolution. Has to be a power of two (0.25, 0.5, 1, 2, etc.)")]
			public float Supersampling = 1.0f;
		}

		private readonly Water water;
		private readonly WindWaves windWaves;
		private readonly Data data;

		private float foamIntensity = 1.0f;
		private float foamThreshold = 1.0f;
		private float foamFadingFactor = 0.85f;
		private float foamShoreExtent;
		private bool foamIntensityOverriden;

		private Shader localFoamSimulationShader;
		private Shader globalFoamSimulationShader;

		private RenderTexture foamMapA;
		private RenderTexture foamMapB;
		private RenderTexture[] displacementDeltaMaps;
		private int resolution;
		private bool firstFrame;

		private readonly DynamicWater overlays;
		private readonly Material globalFoamSimulationMaterial;

		private static int foamParametersId;
		private static int foamShoreIntensityId;
		private static int foamIntensityId;
		private static int waterTileSizeInvSRTId;
		private static int[] displacementDeltaMapsIds;
		private static readonly Dictionary<WaterCamera, CameraRenderData> layerUpdateFrames = new Dictionary<WaterCamera, CameraRenderData>();

		public Foam(Water water, Data data)
		{
			this.water = water;
			this.windWaves = water.WindWaves;
			this.overlays = water.DynamicWater;
			this.data = data;

			Validate();

			if (displacementDeltaMapsIds == null)
				ComputeShaderIds();

			windWaves.ResolutionChanged.AddListener(OnResolutionChanged);

			resolution = Mathf.RoundToInt(windWaves.FinalResolution * data.Supersampling);
			globalFoamSimulationMaterial = new Material(globalFoamSimulationShader) { hideFlags = HideFlags.DontSave };

			firstFrame = true;
		}

		internal void Enable()
		{
			water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
			OnProfilesChanged(water);
		}

		internal void Disable()
		{
			water.ProfilesManager.Changed.RemoveListener(OnProfilesChanged);
		}

		public float FoamIntensity
		{
			get { return foamIntensity; }
			set
			{
				if(float.IsNaN(value))
				{
					foamIntensityOverriden = false;
					OnProfilesChanged(water);
				}
				else
				{
					foamIntensityOverriden = true;
					foamIntensity = value;

					if (globalFoamSimulationMaterial != null)
					{
						float tl = foamThreshold * resolution / 2048.0f * 0.5f;
						globalFoamSimulationMaterial.SetVector(foamParametersId, new Vector4(foamIntensity*0.6f, tl, 0.0f, foamFadingFactor));
					}

					var block = water.Renderer.PropertyBlock;
					float t = foamThreshold * resolution / 2048.0f * 0.5f;
					block.SetVector(foamParametersId, new Vector4(foamIntensity * 0.6f, t, 150.0f / (foamShoreExtent * foamShoreExtent), foamFadingFactor));
				}
			}
		}

		public Texture FoamMap
		{
			get { return foamMapA; }
		}

		private void SetupFoamMaterials()
		{
			if(globalFoamSimulationMaterial != null)
			{
				float tl = foamThreshold * resolution / 2048.0f * 0.5f;
				float tg = tl * 220.0f;
				globalFoamSimulationMaterial.SetVector(foamParametersId, new Vector4(foamIntensity * 0.6f, tl, 0.0f, foamFadingFactor));
				globalFoamSimulationMaterial.SetVector(foamIntensityId, new Vector4(tg / windWaves.TileSizes.x, tg / windWaves.TileSizes.y, tg / windWaves.TileSizes.z, tg / windWaves.TileSizes.w));
			}
		}

		internal void Validate()
		{
			if(globalFoamSimulationShader == null)
				globalFoamSimulationShader = Shader.Find("PlayWay Water/Foam/Global");

			if(localFoamSimulationShader == null)
				localFoamSimulationShader = Shader.Find("PlayWay Water/Foam/Local");

			data.Supersampling = Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(data.Supersampling * 4096)) / 4096.0f;
		}

		internal void Destroy()
		{
			if(foamMapA != null)
			{
				foamMapA.Destroy();
				foamMapB.Destroy();

				foamMapA = null;
				foamMapB = null;
			}

			if (displacementDeltaMaps != null)
			{
				for (int i = 0; i < displacementDeltaMaps.Length; ++i)
					displacementDeltaMaps[i].Destroy();

				displacementDeltaMaps = null;
			}
		}

		internal void Update()
		{
			if(!firstFrame && overlays == null)
				UpdateFoamTiled();
			else
				firstFrame = false;
		}

		private void CheckTilesFoamResources()
		{
			if(foamMapA == null)
			{
				foamMapA = CreateRT(0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, FilterMode.Trilinear, TextureWrapMode.Repeat);
				foamMapB = CreateRT(0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, FilterMode.Trilinear, TextureWrapMode.Repeat);

				RenderTexture.active = null;
			}
		}

		private RenderTexture CreateRT(int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite, FilterMode filterMode, TextureWrapMode wrapMode)
		{
			var renderTexture = new RenderTexture(resolution, resolution, depth, format, readWrite)
			{
				hideFlags = HideFlags.DontSave,
				filterMode = filterMode,
				wrapMode = wrapMode,
				useMipMap = true,
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4
				generateMips = true
#else
				autoGenerateMips = true
#endif
			};

			RenderTexture.active = renderTexture;
			GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

			return renderTexture;
		}

		private void UpdateFoamTiled()
		{
			if(!CheckPreresquisites())
				return;

			CheckTilesFoamResources();
			SetupFoamMaterials();

			var waterWavesFFT = windWaves.WaterWavesFFT;
			globalFoamSimulationMaterial.SetTexture("_DisplacementMap0", waterWavesFFT.GetDisplacementMap(0));
			globalFoamSimulationMaterial.SetTexture("_DisplacementMap1", waterWavesFFT.GetDisplacementMap(1));
			globalFoamSimulationMaterial.SetTexture("_DisplacementMap2", waterWavesFFT.GetDisplacementMap(2));
			globalFoamSimulationMaterial.SetTexture("_DisplacementMap3", waterWavesFFT.GetDisplacementMap(3));
			Graphics.Blit(foamMapA, foamMapB, globalFoamSimulationMaterial, 0);

			water.Renderer.PropertyBlock.SetTexture("_FoamMap", foamMapB);

			SwapRenderTargets();
		}

		private void OnResolutionChanged(WindWaves windWaves)
		{
			resolution = Mathf.RoundToInt(windWaves.FinalResolution * data.Supersampling);

			Destroy();
		}

		private bool CheckPreresquisites()
		{
			return windWaves != null && windWaves.FinalRenderMode == WaveSpectrumRenderMode.FullFFT;
		}

		private void OnProfilesChanged(Water water)
		{
			var profiles = water.ProfilesManager.Profiles;

			float foamIntensity = 0.0f;
			foamThreshold = 0.0f;
			foamFadingFactor = 0.0f;
			foamShoreExtent = 0.0f;
			float foamShoreIntensity = 0.0f;
			float foamNormalScale = 0.0f;

			if(profiles != null)
			{
				for(int i = profiles.Length - 1; i >= 0; --i)
				{
					var weightedProfile = profiles[i];
					var profile = weightedProfile.Profile;
					float weight = weightedProfile.Weight;

					foamIntensity += profile.FoamIntensity * weight;
					foamThreshold += profile.FoamThreshold * weight;
					foamFadingFactor += profile.FoamFadingFactor * weight;
					foamShoreExtent += profile.FoamShoreExtent * weight;
					foamShoreIntensity += profile.FoamShoreIntensity * weight;
					foamNormalScale += profile.FoamNormalScale * weight;
				}
			}

			if(!foamIntensityOverriden) this.foamIntensity = foamIntensity;

			var block = water.Renderer.PropertyBlock;
			block.SetFloat("_FoamNormalScale", foamNormalScale);

			if(foamShoreExtent < 0.001f)
				foamShoreExtent = 0.001f;

			float t = foamThreshold * resolution / 2048.0f * 0.5f;
			block.SetVector(foamParametersId, new Vector4(foamIntensity * 0.6f, t, 150.0f / (foamShoreExtent * foamShoreExtent), foamFadingFactor));
			block.SetFloat(foamShoreIntensityId, foamShoreIntensity);
		}

		private void SwapRenderTargets()
		{
			var t = foamMapA;
			foamMapA = foamMapB;
			foamMapB = t;
		}

		public void RenderOverlays(DynamicWaterCameraData overlays)
		{
			if(!Application.isPlaying || !CheckPreresquisites())
				return;

			var waterCamera = overlays.Camera;

			if(waterCamera.Type != WaterCamera.CameraType.Normal)
				return;

			int layer = water.gameObject.layer;
			CameraRenderData cameraRenderData;

			if(!layerUpdateFrames.TryGetValue(waterCamera, out cameraRenderData))
			{
				layerUpdateFrames[waterCamera] = cameraRenderData = new CameraRenderData();
				waterCamera.Destroyed += OnCameraDestroyed;
			}

			int frameCount = Time.frameCount;

			if(cameraRenderData.RenderFramePerLayer[layer] < frameCount)
			{
				cameraRenderData.RenderFramePerLayer[layer] = frameCount;

				if (water.WindWaves.FinalRenderMode == WaveSpectrumRenderMode.FullFFT)
				{
					var displacementDeltaMaps = GetDisplacementDeltaMaps();

					float t = foamThreshold * resolution / 2048.0f * 0.5f;
					globalFoamSimulationMaterial.SetVector(foamParametersId, new Vector4(foamIntensity * 0.6f, t, 0.0f, foamFadingFactor));

					for (int i = 0; i < 4; ++i)
					{
						var displacementMap = water.WindWaves.WaterWavesFFT.GetDisplacementMap(i);
						var displacementDeltaMap = displacementDeltaMaps[i];
						
						globalFoamSimulationMaterial.SetFloat(waterTileSizeInvSRTId, water.WindWaves.TileSizesInv[i]);
						Graphics.Blit(displacementMap, displacementDeltaMap, globalFoamSimulationMaterial, 1);
					}

					Shader.SetGlobalTexture("_FoamMapPrevious", overlays.FoamMapPrevious);
					Shader.SetGlobalVector("_WaterOffsetDelta", water.SurfaceOffset - cameraRenderData.LastSurfaceOffset);
					cameraRenderData.LastSurfaceOffset = water.SurfaceOffset;

					var projectorCamera = waterCamera.PlaneProjectorCamera;
					projectorCamera.cullingMask = 1 << layer;

					projectorCamera.GetComponent<WaterCamera>()
						.RenderWaterWithShader("[PW Water] Foam", overlays.FoamMap, localFoamSimulationShader, water);
				}
			}

			water.Renderer.PropertyBlock.SetTexture("_FoamMap", overlays.FoamMap);
		}

		private RenderTexture[] GetDisplacementDeltaMaps()
		{
			if (displacementDeltaMaps == null)
			{
				displacementDeltaMaps = new RenderTexture[4];
				bool allowFloatingPointMipMaps = WaterProjectSettings.Instance.AllowFloatingPointMipMaps;

				for (int i = 0; i < 4; ++i)
				{
					displacementDeltaMaps[i] = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
					{
						useMipMap = allowFloatingPointMipMaps,
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4
						generateMips = allowFloatingPointMipMaps,
#else
						autoGenerateMips = allowFloatingPointMipMaps,
#endif
						wrapMode = TextureWrapMode.Repeat,
						filterMode = allowFloatingPointMipMaps ? FilterMode.Trilinear : FilterMode.Bilinear
					};

					water.Renderer.PropertyBlock.SetTexture(displacementDeltaMapsIds[i], displacementDeltaMaps[i]);
				}
			}

			return displacementDeltaMaps;
		}

		private static void ComputeShaderIds()
		{
			foamParametersId = Shader.PropertyToID("_FoamParameters");
			foamShoreIntensityId = Shader.PropertyToID("_FoamShoreIntensity");
			foamIntensityId = Shader.PropertyToID("_FoamIntensity");
			waterTileSizeInvSRTId = Shader.PropertyToID("_WaterTileSizeInvSRT");

			displacementDeltaMapsIds = new[]
			{
				Shader.PropertyToID("_DisplacementDeltaMap"),
				Shader.PropertyToID("_DisplacementDeltaMap1"),
				Shader.PropertyToID("_DisplacementDeltaMap2"),
				Shader.PropertyToID("_DisplacementDeltaMap3")
			};
		}

		private static void OnCameraDestroyed(WaterCamera waterCamera)
		{
			layerUpdateFrames.Remove(waterCamera);
		}

		private class CameraRenderData
		{
			public readonly int[] RenderFramePerLayer = new int[32];
			public Vector2 LastSurfaceOffset;
		}
	}
}
