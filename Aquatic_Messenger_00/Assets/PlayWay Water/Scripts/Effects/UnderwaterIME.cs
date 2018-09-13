using PlayWay.Water.Internal;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	/// <summary>
	/// Underwater image effect.
	/// </summary>
	[ExecuteInEditMode]
	[RequireComponent(typeof(Camera))]
	[RequireComponent(typeof(WaterCamera))]
	public sealed class UnderwaterIME : MonoBehaviour, IWaterImageEffect
	{
		[HideInInspector][SerializeField] private Shader underwaterMaskShader;
		[HideInInspector][SerializeField] private Shader imeShader;
		[HideInInspector][SerializeField] private Shader noiseShader;
		[HideInInspector][SerializeField] private Shader composeUnderwaterMaskShader;

		[SerializeField]
		private Blur blur;

		[SerializeField]
		private bool underwaterAudio = true;

		[Tooltip("Individual camera blur scale. It's recommended to modify blur scale through water profiles. Use this one, only if some of your cameras need a clear view and some don't.")]
		[Range(0.0f, 4.0f)]
		[SerializeField]
		private float cameraBlurScale = 1.0f;

		[Range(0.1f, 1.0f)]
		[SerializeField]
		private float maskResolution = 0.25f;

		[SerializeField]
		private bool renderInEditMode;
		
		private Material maskMaterial;
		private Material imeMaterial;
		private Material noiseMaterial;
		private Material composeUnderwaterMaskMaterial;

		private Camera localCamera;
		private WaterCamera localWaterCamera;
		private AudioReverbFilter reverbFilter;
		private CommandBuffer maskCommandBuffer;

		private float intensity = float.NaN;
		private bool renderUnderwaterMask;
		private Water waterOverride;
		private bool hasWaterOverride;
		private bool effectEnabled = true;
		private int maskRT, maskRT2;

		private void Awake()
		{
			localCamera = GetComponent<Camera>();
			localWaterCamera = GetComponent<WaterCamera>();

			maskRT = Shader.PropertyToID("_UnderwaterMask");
			maskRT2 = Shader.PropertyToID("_UnderwaterMask2");

			OnValidate();

			maskMaterial = new Material(underwaterMaskShader) {hideFlags = HideFlags.DontSave};
			imeMaterial = new Material(imeShader) {hideFlags = HideFlags.DontSave};
			noiseMaterial = new Material(noiseShader) {hideFlags = HideFlags.DontSave};
			composeUnderwaterMaskMaterial = new Material(composeUnderwaterMaskShader) {hideFlags = HideFlags.DontSave};

			reverbFilter = GetComponent<AudioReverbFilter>();

			if(reverbFilter == null && underwaterAudio)
				reverbFilter = gameObject.AddComponent<AudioReverbFilter>();
		}

		public Blur Blur
		{
			get { return blur; }
		}
		
		public float Intensity
		{
			get { return intensity; }
		}

		public bool EffectEnabled
		{
			get { return effectEnabled; }
			set { effectEnabled = value; }
		}

		public Water WaterOverride
		{
			get { return waterOverride; }
			set
			{
				waterOverride = value;
				hasWaterOverride = value != null;
				OnSubmersionStateChanged(localWaterCamera);
			}
		}

		// Called by WaterCamera.cs
		public void OnWaterCameraEnabled()
		{
			var waterCamera = GetComponent<WaterCamera>();
			waterCamera.SubmersionStateChanged.AddListener(OnSubmersionStateChanged);
		}

		// Called by WaterCamera.cs, to update this effect when it's disabled
		public void OnWaterCameraPreCull()
		{
			if(!effectEnabled)
			{
				enabled = false;
				return;
			}

			if (hasWaterOverride)
			{
				enabled = true;
				renderUnderwaterMask = true;
				return;
			}
			
			switch(localWaterCamera.SubmersionState)
			{
				case SubmersionState.None:
				{
					enabled = false;
					break;
				}

				case SubmersionState.Partial:
				{
					enabled = true;
					renderUnderwaterMask = true;
					break;
				}

				case SubmersionState.Full:
				{
					enabled = true;
					renderUnderwaterMask = false;
					break;
				}
			}

			float nearPlaneSizeY = localCamera.nearClipPlane * Mathf.Tan(localCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
			float verticalDistance = transform.position.y - localWaterCamera.WaterLevel;
			float intensity = (-verticalDistance + nearPlaneSizeY) * 0.25f;
			SetEffectsIntensity(intensity);
		}

		private void OnDisable()
		{
			if(maskCommandBuffer != null)
				maskCommandBuffer.Clear();
		}

		private void OnDestroy()
		{
			if(maskCommandBuffer != null)
			{
				maskCommandBuffer.Dispose();
				maskCommandBuffer = null;
			}

			if(blur != null)
				blur.Dispose();

			maskMaterial.Destroy();
			imeMaterial.Destroy();
		}

		private void OnValidate()
		{
			if(underwaterMaskShader == null)
				underwaterMaskShader = Shader.Find("PlayWay Water/Underwater/Screen-Space Mask");

			if(imeShader == null)
				imeShader = Shader.Find("PlayWay Water/Underwater/Base IME");

			if(noiseShader == null)
				noiseShader = Shader.Find("PlayWay Water/Utilities/Noise");

			if(composeUnderwaterMaskShader == null)
				composeUnderwaterMaskShader = Shader.Find("PlayWay Water/Underwater/Compose Underwater Mask");

			if(blur != null)
				blur.Validate("PlayWay Water/Utilities/Blur (Underwater)");
		}

		private void OnPreCull()
		{
			RenderUnderwaterMask();
		}

		//[ImageEffectOpaque]			// it will be an opaque effect in the future
		private void OnRenderImage(RenderTexture source, RenderTexture destination)
		{
#if UNITY_EDITOR
			if (!renderInEditMode && !Application.isPlaying)
			{
				Graphics.Blit(source, destination);
				return;
			}
#endif

			var containingWater = hasWaterOverride ? waterOverride : localWaterCamera.ContainingWater;

			if(!localWaterCamera.enabled || containingWater == null)
			{
				Graphics.Blit(source, destination);
				return;
			}

			source.filterMode = FilterMode.Bilinear;
			
			var temp1 = RenderTexturesCache.GetTemporary(source.width, source.height, 0, destination != null ? destination.format : source.format, true, false);
			temp1.Texture.filterMode = FilterMode.Bilinear;
			temp1.Texture.wrapMode = TextureWrapMode.Clamp;

			RenderDepthScatter(source, temp1);

			blur.TotalSize = containingWater.Materials.UnderwaterBlurSize * cameraBlurScale;
			blur.Apply(temp1);

			RenderDistortions(temp1, destination);
			temp1.Dispose();
		}

		private void RenderUnderwaterMask()
		{
			if(maskCommandBuffer == null)
				return;

			maskCommandBuffer.Clear();

			var containingWater = hasWaterOverride ? waterOverride : localWaterCamera.ContainingWater;
			var currentCamera = Camera.current;

			if(renderUnderwaterMask || (containingWater != null && containingWater.Renderer.MaskCount > 0))
			{
				int w = Mathf.RoundToInt(currentCamera.pixelWidth * maskResolution);
				int h = Mathf.RoundToInt(currentCamera.pixelHeight * maskResolution);
				maskCommandBuffer.GetTemporaryRT(maskRT, w, h, 0, FilterMode.Bilinear, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1);
				maskCommandBuffer.GetTemporaryRT(maskRT2, w, h, 0, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1);
			}
			else
				maskCommandBuffer.GetTemporaryRT(maskRT, 4, 4, 0, FilterMode.Point, RenderTextureFormat.R8, RenderTextureReadWrite.Linear, 1);
			
			if(renderUnderwaterMask && containingWater != null)
			{
				maskMaterial.CopyPropertiesFromMaterial(containingWater.Materials.SurfaceMaterial);

				maskCommandBuffer.SetRenderTarget(maskRT2);
				maskCommandBuffer.ClearRenderTarget(false, true, Color.black);
				
				Matrix4x4 matrix;
				var geometry = containingWater.Geometry;
				var meshes = geometry.GetTransformedMeshes(localCamera, out matrix, geometry.GeometryType == WaterGeometry.Type.ProjectionGrid ? WaterGeometryType.RadialGrid : WaterGeometryType.Auto, true, geometry.ComputeVertexCountForCamera(currentCamera));

				for(int i=meshes.Length-1; i>=0; --i)
					maskCommandBuffer.DrawMesh(meshes[i], matrix, maskMaterial, 0, 0, containingWater.Renderer.PropertyBlock);
				
				// filter out common artifacts from the mask
				//maskCommandBuffer.Blit(maskRT2, maskRT, imeMaterial, 4);
				maskCommandBuffer.SetRenderTarget(maskRT);
				maskCommandBuffer.DrawMesh(Quads.BipolarXInversedY, Matrix4x4.identity, imeMaterial, 0, 3, containingWater.Renderer.PropertyBlock);
				maskCommandBuffer.ReleaseTemporaryRT(maskRT2);
			}
			else
			{
				maskCommandBuffer.SetRenderTarget(maskRT);
				maskCommandBuffer.ClearRenderTarget(false, true, Color.white);
			}

			if(containingWater != null && containingWater.Renderer.MaskCount != 0)
			{
				if(localWaterCamera.RenderVolumes)
					maskCommandBuffer.Blit("_SubtractiveMask", maskRT, composeUnderwaterMaskMaterial, 0);
			}

			var evt = localCamera.actualRenderingPath == RenderingPath.Forward ? localWaterCamera.SinglePassStereoRendering ? CameraEvent.BeforeForwardOpaque : CameraEvent.AfterDepthTexture : CameraEvent.BeforeLighting;
            localCamera.RemoveCommandBuffer(evt, maskCommandBuffer);
            localCamera.AddCommandBuffer(evt, maskCommandBuffer);
		}

		private void RenderDepthScatter(RenderTexture source, RenderTexture target)
		{
			/*Color totalDirectionalLightContribution = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			var lights = LightWaterEffects.lights;

			for (int i = lights.Count - 1; i >= 0; --i)
			{
				var light = lights[i].UnityLight;
				totalDirectionalLightContribution += Mathf.Max(0.0f, -Vector3.Dot(light.transform.forward, Vector3.up))*light.intensity*lights[i].ScatteringIntensity*light.color;
			}*/

			var containingWater = hasWaterOverride ? waterOverride : localWaterCamera.ContainingWater;
			imeMaterial.CopyPropertiesFromMaterial(containingWater.Materials.SurfaceMaterial);
			imeMaterial.SetTexture("_UnderwaterAbsorptionGradient", containingWater.Materials.UnderwaterAbsorptionColorByDepth);
			imeMaterial.SetFloat("_UnderwaterLightFadeScale", containingWater.Materials.UnderwaterLightFadeScale);
			//imeMaterial.SetColor("_TotalDirectionalLightsContribution", totalDirectionalLightContribution * 0.5f);
			imeMaterial.SetMatrix("UNITY_MATRIX_VP_INVERSE", Matrix4x4.Inverse(localCamera.projectionMatrix * localCamera.worldToCameraMatrix));

			var block = containingWater.Renderer.PropertyBlock;
			//Vector4 originalAbsorptionColor = block.GetVector("_AbsorptionColor");
			//Vector2 surfaceOffset2D = containingWater.SurfaceOffset;
			//block.SetVector("_SurfaceOffset", new Vector4(surfaceOffset2D.x, containingWater.transform.position.y, surfaceOffset2D.y, containingWater.UniformWaterScale));

			//Graphics.Blit(source, target, imeMaterial, 2);
			GraphicsUtilities.Blit(source, target, imeMaterial, 1, block);

			//block.SetVector("_AbsorptionColor", originalAbsorptionColor);
		}

		private void RenderDistortions(RenderTexture source, RenderTexture target)
		{
			var containingWater = hasWaterOverride ? waterOverride : localWaterCamera.ContainingWater;
			float distortionIntensity = containingWater.Materials.UnderwaterDistortionsIntensity;

			if(distortionIntensity > 0.0f)
			{
				int w = Camera.current.pixelWidth >> 2;
				int h = Camera.current.pixelHeight >> 2;
				var distortionTex = RenderTexturesCache.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, true, false);
				RenderDistortionMap(distortionTex);

				imeMaterial.SetTexture("_DistortionTex", distortionTex);
				imeMaterial.SetFloat("_DistortionIntensity", distortionIntensity);
				GraphicsUtilities.Blit(source, target, imeMaterial, 2, containingWater.Renderer.PropertyBlock);
				//Graphics.Blit(source, target, imeMaterial, 2);

				distortionTex.Dispose();
			}
			else
				Graphics.Blit(source, target);
		}

		private void RenderDistortionMap(RenderTexture target)
		{
			var containingWater = hasWaterOverride ? waterOverride : localWaterCamera.ContainingWater;
			noiseMaterial.SetVector("_Offset", new Vector4(0.0f, 0.0f, Time.time * containingWater.Materials.UnderwaterDistortionAnimationSpeed, 0.0f));
			noiseMaterial.SetVector("_Period", new Vector4(4, 4, 4, 4));
			Graphics.Blit(null, target, noiseMaterial, 1);
		}

		private void OnSubmersionStateChanged(WaterCamera waterCamera)
		{
			if(waterCamera.SubmersionState != SubmersionState.None || hasWaterOverride)
			{
				if(maskCommandBuffer == null)
				{
					maskCommandBuffer = new CommandBuffer {name = "Render Underwater Mask"};
				}
			}
			else
			{
				if(maskCommandBuffer != null)			// remove command buffer if camera is out of water
				{
					var camera = GetComponent<Camera>();
					camera.RemoveCommandBuffer(localWaterCamera.SinglePassStereoRendering ? CameraEvent.BeforeForwardOpaque : CameraEvent.AfterDepthTexture, maskCommandBuffer);
					camera.RemoveCommandBuffer(CameraEvent.AfterLighting, maskCommandBuffer);
				}
			}
		}
		
		private void SetEffectsIntensity(float intensity)
		{
			if(localCamera == null)          // start wasn't called yet
				return;

			intensity = Mathf.Clamp01(intensity);

			if(this.intensity == intensity)
				return;
			
			this.intensity = intensity;

			if(reverbFilter != null && underwaterAudio)
			{
				float reverbIntensity = intensity > 0.05f ? Mathf.Clamp01(intensity + 0.7f) : intensity;

				reverbFilter.dryLevel = -2000 * reverbIntensity;
				reverbFilter.room = -10000 * (1.0f - reverbIntensity);
				reverbFilter.roomHF = Mathf.Lerp(-10000, -4000, reverbIntensity);
				reverbFilter.decayTime = 1.6f * reverbIntensity;
				reverbFilter.decayHFRatio = 0.1f * reverbIntensity;
				reverbFilter.reflectionsLevel = -449.0f * reverbIntensity;
				reverbFilter.reverbLevel = 1500.0f * reverbIntensity;
				reverbFilter.reverbDelay = 0.0259f * reverbIntensity;
			}
		}
	}
}
