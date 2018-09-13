using System.Collections.Generic;
using PlayWay.Water.Internal;
using UnityEngine;

namespace PlayWay.Water
{
	[System.Serializable]
	public sealed class WaterSubsurfaceScattering
	{
		[HideInInspector]
		[SerializeField]
		private Shader collectLightShader;

		[HideInInspector]
		[SerializeField]
		private Shader collectTransmissionShader;

		[SerializeField]
		private SubsurfaceScatteringMode mode = SubsurfaceScatteringMode.TextureSpace;

		[SerializeField]
		private BlurSSS subsurfaceScatteringBlur;
		
		[Range(0.0f, 0.9f)]
		[SerializeField]
		private float ignoredLightFraction = 0.15f;

		[Resolution(128, 64, 128, 256, 512)]
		[SerializeField]
		private int ambientResolution = 128;

		[Range(-1, 6)]
		[SerializeField]
		private int lightCount = -1;

		[SerializeField]
		private int lightingLayer = 22;
		
		private RenderTexture scatteringTex;
		private Vector4 shaderParams;				// x = intensity, y = contrast
		private Water water;

		private static List<Water> cachedRenderList;

		static WaterSubsurfaceScattering()
		{
			cachedRenderList = new List<Water>();
			cachedRenderList.Add(null);
		}

		internal void Start(Water water)
		{
			this.water = water;

			water.ProfilesManager.Changed.AddListener(ResolveProfileData);
		}

		internal void Enable()
		{
			Validate();

			if(Application.isPlaying && mode == SubsurfaceScatteringMode.TextureSpace)
			{
				scatteringTex = new RenderTexture(ambientResolution, ambientResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
				{
					name = "Water Subsurface Scattering",
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Bilinear,
					useMipMap = WaterProjectSettings.Instance.AllowFloatingPointMipMaps,
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3 || UNITY_5_4
					generateMips = WaterProjectSettings.Instance.AllowFloatingPointMipMaps
#else
					autoGenerateMips = WaterProjectSettings.Instance.AllowFloatingPointMipMaps
#endif
				};
			}
        }

		internal void Disable()
		{
			if(scatteringTex != null)
			{
				scatteringTex.Destroy();
				scatteringTex = null;
			}

			if(water != null)
				water.Renderer.PropertyBlock.SetTexture("_SubsurfaceScattering", DefaultTextures.WhiteTexture);
		}

		public float IsotropicScatteringIntensity
		{
			get { return shaderParams.x; }
			set { shaderParams.x = value; }
		}

		public float SubsurfaceScatteringContrast
		{
			get { return shaderParams.y; }
			set { shaderParams.y = value; }
		}

		internal void Destroy()
		{
			if(water != null)
				water.ProfilesManager.Changed.RemoveListener(ResolveProfileData);
		}

		internal void ResolveProfileData(Water water)
		{
			var profiles = water.ProfilesManager.Profiles;
			shaderParams.x = 0.0f;
			shaderParams.y = 0.0f;

			for(int i=0; i<profiles.Length; ++i)
			{
				var profile = profiles[i].Profile;
				float weight = profiles[i].Weight;

				shaderParams.x += profile.IsotropicScatteringIntensity * weight;
				shaderParams.y += profile.SubsurfaceScatteringContrast * weight;
			}

			shaderParams.x *= 1.0f + shaderParams.y;
		}

		internal void Validate()
		{
			if(collectLightShader == null)
				collectLightShader = Shader.Find("PlayWay Water/Refraction/Collect Light");

			if(collectTransmissionShader == null)
				collectTransmissionShader = Shader.Find("PlayWay Water/Refraction/Transmission");

			if(subsurfaceScatteringBlur == null)
				subsurfaceScatteringBlur = new BlurSSS();

			subsurfaceScatteringBlur.Validate("PlayWay Water/Utilities/Blur (Subsurface Scattering)", "Shaders/Blurs", 6);
		}

		public void OnWaterRender(Camera camera)
		{
			var waterCamera = WaterCamera.GetWaterCamera(camera);
			Rect rect = waterCamera.LocalMapsRect;

			if(rect.width == 0.0f || !Application.isPlaying || mode == SubsurfaceScatteringMode.Disabled)
				return;

			var temp1 = RenderTexture.GetTemporary(ambientResolution, ambientResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
			temp1.filterMode = FilterMode.Bilinear;

			//waterCamera.enabled = true;
			var effectsCamera = waterCamera.EffectsCamera;

			var effectsWaterCamera = effectsCamera.GetComponent<WaterCamera>();
			effectsWaterCamera.enabled = true;
			effectsWaterCamera.GeometryType = WaterGeometryType.UniformGrid;

			cachedRenderList[0] = water;
			effectsWaterCamera.SetCustomWaterRenderList(cachedRenderList);
			
			effectsCamera.stereoTargetEye = StereoTargetEyeMask.None;
			effectsCamera.enabled = false;
			effectsCamera.depthTextureMode = DepthTextureMode.None;
			effectsCamera.renderingPath = RenderingPath.Forward;
			effectsCamera.orthographic = true;
			effectsCamera.orthographicSize = rect.width * 0.5f;
			effectsCamera.cullingMask = 1 << lightingLayer;
			effectsCamera.farClipPlane = 2000.0f;
			effectsCamera.ResetProjectionMatrix();
			effectsCamera.clearFlags = CameraClearFlags.Nothing;
#if UNITY_5_6
			effectsCamera.allowHDR = true;
#else
			effectsCamera.hdr = true;
#endif
			effectsCamera.transform.position = new Vector3(rect.center.x, 1000.0f, rect.center.y);
			effectsCamera.transform.rotation = Quaternion.LookRotation(new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f));
			
			effectsCamera.targetTexture = temp1;

			Shader.SetGlobalVector("_ScatteringParams", shaderParams);
			Shader.SetGlobalVector("_WorldSpaceOriginalCameraPos", camera.transform.position);

			int originalPixelLightCount = 3;

			if(lightCount >= 0)
			{
				originalPixelLightCount = QualitySettings.pixelLightCount;
				QualitySettings.pixelLightCount = lightCount;
			}
			
			water.gameObject.layer = lightingLayer;
			effectsCamera.RenderWithShader(collectLightShader, "CustomType");
			water.gameObject.layer = WaterProjectSettings.Instance.WaterLayer;

			if(lightCount >= 0)
				QualitySettings.pixelLightCount = originalPixelLightCount;

			effectsWaterCamera.GeometryType = WaterGeometryType.Auto;
			effectsWaterCamera.SetCustomWaterRenderList(null);

			var temp2 = RenderTexture.GetTemporary(ambientResolution, ambientResolution, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
			temp2.filterMode = FilterMode.Point;

			Color absorptionColor = water.Materials.GetParameterValue(WaterMaterials.ColorParameter.AbsorptionColor);
			subsurfaceScatteringBlur.BlurMaterial.SetVector("_ScatteringParams", shaderParams);
			subsurfaceScatteringBlur.Apply(temp1, temp2, absorptionColor, waterCamera.LocalMapsRect.width, ignoredLightFraction);

			RenderTexture.ReleaseTemporary(temp1);

			Graphics.Blit(temp2, scatteringTex, subsurfaceScatteringBlur.BlurMaterial, 1);
			RenderTexture.ReleaseTemporary(temp2);

			water.Renderer.PropertyBlock.SetTexture("_SubsurfaceScattering", scatteringTex);
		}

		public enum SubsurfaceScatteringMode
		{
			Disabled,
			TextureSpace
		}
	}
}
