using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	/// <summary>
	///		Manages all materials used for water surface rendering and manages their most basic properties.
	/// </summary>
	[Serializable]
	public class WaterMaterials
	{
		public enum ColorParameter
		{
			AbsorptionColor = 0,
			DiffuseColor = 1,
			SpecularColor = 2,
			DepthColor = 3,
			EmissionColor = 4,
			ReflectionColor = 5
		}

		public enum FloatParameter
		{
			DisplacementScale = 6,
			Glossiness = 7,
			RefractionDistortion = 10,
			SpecularFresnelBias = 11,
			DisplacementNormalsIntensity = 13,
			EdgeBlendFactorInv = 14,
			LightSmoothnessMultiplier = 18
		}

		public enum VectorParameter
		{
			/// <summary>
			/// x = subsurfaceScattering, y = 0.15f, z = 1.65f, w = unused
			/// </summary>
			SubsurfaceScatteringPack = 8,

			/// <summary>
			/// x = directionalWrapSSS, y = 1.0f / (1.0f + directionalWrapSSS), z = pointWrapSSS, w = 1.0f / (1.0f + pointWrapSSS)
			/// </summary>
			WrapSubsurfaceScatteringPack = 9,
			DetailFadeFactor = 12,
			PlanarReflectionPack = 15,
			BumpScale = 16,
			FoamTiling = 17
		}

		public enum TextureParameter
		{
			BumpMap = 19,
			FoamTex = 20,
			FoamNormalMap = 21
		}

		[SerializeField]
		private float directionalLightsIntensity = 1.0f;

		[Tooltip("May hurt performance on some systems.")]
		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float tesselationFactor = 1.0f;

		private Water water;

		private float horizontalDisplacementScale;
		private float underwaterBlurSize;
		private float underwaterLightFadeScale;
		private float underwaterDistortionsIntensity;
		private float underwaterDistortionAnimationSpeed;
		private float smoothness, ambientSmoothness;
		private float forwardScatteringIntensity;
		private Vector4 lastSurfaceOffset;
		private Texture2D absorptionGradient;
		private bool absorptionGradientDirty = true;
		private int alphaBlendMode;
		private string usedKeywords;
		
		private WaterParameterFloat[] floatOverrides = new WaterParameterFloat[0];
		private WaterParameterVector4[] vectorOverrides = new WaterParameterVector4[0];

		private const int GradientResolution = 64;
		private static Color[] absorptionColorsBuffer = new Color[GradientResolution];

		private static int surfaceOffsetId;
		private static int waterShaderId;
		private static int cullId;
		private static int[] parameterHashes;

		private static Texture2D globalWaterLookupTex;
		private static bool globalLookupDirty;

		private static readonly string[] parameterNames = {
			"_AbsorptionColor", "_Color", "_SpecColor", "_DepthColor", "_EmissionColor", "_ReflectionColor", "_DisplacementsScale", "_Glossiness",
			"_SubsurfaceScatteringPack", "_WrapSubsurfaceScatteringPack", "_RefractionDistortion", "_SubsurfaceScatteringShoreColor", "_DetailFadeFactor",
			"_DisplacementNormalsIntensity", "_EdgeBlendFactorInv", "_PlanarReflectionPack", "_BumpScale", "_FoamTiling", "_LightSmoothnessMul",
			"_BumpMap", "_FoamTex", "_FoamNormalMap",
			"_FoamSpecularColor", "_RefractionMaxDepth", "_FoamDiffuseColor"
		};

		internal void Start(Water water)
		{
			this.water = water;

			water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
			water.WaterIdChanged += OnWaterIdChanged;

			CreateParameterHashes();
			CreateMaterials();
		}

		internal void Enable()
		{

		}

		internal void Disable()
		{

		}

		// materials
		public Material SurfaceMaterial { get; private set; }
		public Material SurfaceBackMaterial { get; private set; }
		public Material VolumeMaterial { get; private set; }
		public Material VolumeBackMaterial { get; private set; }
		
		public Texture2D UnderwaterAbsorptionColorByDepth
		{
			get
			{
				if (absorptionGradientDirty)
					ComputeAbsorptionGradient();
				
				return absorptionGradient;
			}
		}

		public float HorizontalDisplacementScale
		{
			get { return horizontalDisplacementScale; }
		}

		public string UsedKeywords
		{
			get { return usedKeywords; }
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

		#region Parameter Overrides
		/// <summary>
		/// Returns current value of some water parameter.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		public Color GetParameterValue(ColorParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < vectorOverrides.Length; ++i)
			{
				if(vectorOverrides[i].Hash == hash)
					return vectorOverrides[i].Value;
			}

			return water.Renderer.PropertyBlock.GetVector(hash);
		}

		/// <summary>
		/// Returns a class that defines custom value for some water parameter. Value that this class holds will override values that are evaluated from water profiles.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		public WaterParameterVector4 GetParameterOverride(ColorParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < vectorOverrides.Length; ++i)
			{
				if(vectorOverrides[i].Hash == hash)
					return vectorOverrides[i];
			}

			Vector4 defaultValue = water.Renderer.PropertyBlock.GetVector(hash);
			Array.Resize(ref vectorOverrides, vectorOverrides.Length + 1);
			return vectorOverrides[vectorOverrides.Length - 1] = new WaterParameterVector4(water.Renderer.PropertyBlock, hash, defaultValue);
		}

		/// <summary>
		/// Resets a water parameter so that it will be evaluated from water profiles again.
		/// </summary>
		/// <param name="parameter"></param>
		public void ResetParameter(ColorParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < vectorOverrides.Length; ++i)
			{
				if(vectorOverrides[i].Hash == hash)
					RemoveArrayElementAt(ref vectorOverrides, i);
			}
		}

		/// <summary>
		/// Returns current value of some water parameter.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		public Vector4 GetParameterValue(VectorParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < vectorOverrides.Length; ++i)
			{
				if(vectorOverrides[i].Hash == hash)
					return vectorOverrides[i].Value;
			}

			return water.Renderer.PropertyBlock.GetVector(hash);
		}

		/// <summary>
		/// Returns a class that defines custom value for some water parameter. Value that this class holds will override values that are evaluated from water profiles.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		public WaterParameterVector4 GetParameterOverride(VectorParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < vectorOverrides.Length; ++i)
			{
				if(vectorOverrides[i].Hash == hash)
					return vectorOverrides[i];
			}

			Vector4 defaultValue = water.Renderer.PropertyBlock.GetVector(hash);
			Array.Resize(ref vectorOverrides, vectorOverrides.Length + 1);
			return vectorOverrides[vectorOverrides.Length - 1] = new WaterParameterVector4(water.Renderer.PropertyBlock, hash, defaultValue);
		}

		/// <summary>
		/// Resets a water parameter so that it will be evaluated from water profiles again.
		/// </summary>
		/// <param name="parameter"></param>
		public void ResetParameter(VectorParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < vectorOverrides.Length; ++i)
			{
				if(vectorOverrides[i].Hash == hash)
					RemoveArrayElementAt(ref vectorOverrides, i);
			}
		}

		/// <summary>
		/// Returns current value of some water parameter.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		public float GetParameterValue(FloatParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < floatOverrides.Length; ++i)
			{
				if(floatOverrides[i].Hash == hash)
					return floatOverrides[i].Value;
			}

			return water.Renderer.PropertyBlock.GetFloat(hash);
		}

		/// <summary>
		/// Returns a class that defines custom value for some water parameter. Value that this class holds will override values that are evaluated from water profiles.
		/// </summary>
		/// <param name="parameter"></param>
		/// <returns></returns>
		public WaterParameterFloat GetParameterOverride(FloatParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < floatOverrides.Length; ++i)
			{
				if(floatOverrides[i].Hash == hash)
					return floatOverrides[i];
			}

			float defaultValue = water.Renderer.PropertyBlock.GetFloat(hash);
			Array.Resize(ref floatOverrides, floatOverrides.Length + 1);
			return floatOverrides[floatOverrides.Length - 1] = new WaterParameterFloat(water.Renderer.PropertyBlock, hash, defaultValue);
		}

		/// <summary>
		/// Resets a water parameter so that it will be evaluated from water profiles again.
		/// </summary>
		/// <param name="parameter"></param>
		public void ResetParameter(FloatParameter parameter)
		{
			int hash = parameterHashes[(int)parameter];

			for(int i = 0; i < floatOverrides.Length; ++i)
			{
				if(floatOverrides[i].Hash == hash)
					RemoveArrayElementAt(ref floatOverrides, i);
			}
		}
		#endregion

		public void SetKeyword(string keyword, bool enable)
		{
			if (enable)
			{
				SurfaceMaterial.EnableKeyword(keyword);
				SurfaceBackMaterial.EnableKeyword(keyword);
				VolumeMaterial.EnableKeyword(keyword);
				VolumeBackMaterial.EnableKeyword(keyword);
			}
			else
			{
				SurfaceMaterial.DisableKeyword(keyword);
				SurfaceBackMaterial.DisableKeyword(keyword);
				VolumeMaterial.DisableKeyword(keyword);
				VolumeBackMaterial.DisableKeyword(keyword);
			}
		}

		internal void OnWaterRender(Camera camera)
		{
			Vector2 surfaceOffset2D = water.SurfaceOffset;
			Vector4 surfaceOffset = new Vector4(surfaceOffset2D.x, water.transform.position.y, surfaceOffset2D.y, water.UniformWaterScale);

			if(surfaceOffset.x != lastSurfaceOffset.x || surfaceOffset.y != lastSurfaceOffset.y || surfaceOffset.z != lastSurfaceOffset.z || surfaceOffset.w != lastSurfaceOffset.w)
			{
				lastSurfaceOffset = surfaceOffset;
				water.Renderer.PropertyBlock.SetVector(surfaceOffsetId, surfaceOffset);
				UpdateGlobalLookupTexOffset();
			}

			Shader.SetGlobalColor(parameterHashes[0], GetParameterValue(ColorParameter.AbsorptionColor));			// caustics need that

			var waterCamera = WaterCamera.GetWaterCamera(camera);

			if(waterCamera.Type == WaterCamera.CameraType.Normal)
			{
				int alphaBlendMode;

				if(waterCamera.RenderMode < WaterRenderMode.ImageEffectDeferred)
				{
					var qualityLevel = WaterQualitySettings.Instance.CurrentQualityLevel;
					bool refraction = water.ShaderSet.TransparencyMode == WaterTransparencyMode.Refractive && qualityLevel.allowAlphaBlending;
					alphaBlendMode = refraction ? 2 : 1;
				}
				else
					alphaBlendMode = 1;

				if(this.alphaBlendMode != alphaBlendMode)
					SetBlendMode(alphaBlendMode == 2);
			}
		}

		internal void Destroy()
		{
			SurfaceMaterial.Destroy();
			SurfaceMaterial = null;

			SurfaceBackMaterial.Destroy();
			SurfaceBackMaterial = null;

			VolumeMaterial.Destroy();
			VolumeMaterial = null;

			VolumeBackMaterial.Destroy();
			VolumeBackMaterial = null;

			water.ProfilesManager.Changed.RemoveListener(OnProfilesChanged);
			water.WaterIdChanged -= OnWaterIdChanged;
		}

		internal void Validate()
		{
			UpdateShaders();
			UpdateSurfaceMaterial();
		}

		private void SetBlendMode(bool alphaBlend)
		{
			alphaBlendMode = alphaBlend ? 2 : 1;

			var surfaceMaterial = SurfaceMaterial;
			surfaceMaterial.SetOverrideTag("RenderType", alphaBlend ? "Transparent" : "Opaque");
			surfaceMaterial.SetFloat("_Mode", alphaBlend ? 2 : 0);
			surfaceMaterial.SetInt("_SrcBlend", (int)(alphaBlend ? BlendMode.SrcAlpha : BlendMode.One));
			surfaceMaterial.SetInt("_DstBlend", (int)(alphaBlend ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
			surfaceMaterial.renderQueue = alphaBlend ? 2990 : 2000;       // 2000 - geometry, 3000 - transparent

			UpdateSurfaceBackMaterial();
			UpdateVolumeMaterials();
		}

		private void CreateMaterials()
		{
			if(SurfaceMaterial != null)
				return;
			
			int waterId = water.WaterId;

			Shader surfaceShader;
			Shader volumeShader;
			water.ShaderSet.FindBestShaders(out surfaceShader, out volumeShader);

			if (surfaceShader == null || volumeShader == null)
				throw new InvalidOperationException("Water in a scene '" + water.gameObject.scene.name + "' doesn't contain necessary shaders to function properly. Please open this scene in editor and simply select the water to update its shaders.");

			SurfaceMaterial = new Material(surfaceShader) { hideFlags = HideFlags.DontSave };
			SurfaceMaterial.SetFloat("_WaterStencilId", waterId);
			SurfaceMaterial.SetFloat("_WaterStencilIdInv", (~waterId) & 255);

			// todo: move this to WaterRenderer.cs
			water.Renderer.PropertyBlock.SetVector("_WaterId", new Vector4(1 << waterId, 1 << (waterId + 1), (waterId + 0.5f) / 256.0f, 0));

			UpdateSurfaceMaterial();

			SurfaceBackMaterial = new Material(surfaceShader) { hideFlags = HideFlags.DontSave };
			VolumeMaterial = new Material(volumeShader) { hideFlags = HideFlags.DontSave };
			VolumeBackMaterial = new Material(volumeShader) { hideFlags = HideFlags.DontSave };

			UpdateSurfaceBackMaterial();
			UpdateVolumeMaterials();

			usedKeywords = string.Join(" ", SurfaceMaterial.shaderKeywords);
		}

		private void UpdateShaders()
		{
			Shader surfaceShader;
			Shader volumeShader;
			water.ShaderSet.FindBestShaders(out surfaceShader, out volumeShader);

			if(SurfaceMaterial.shader != surfaceShader || VolumeMaterial.shader != volumeShader)
			{
				SurfaceMaterial.shader = surfaceShader;
				SurfaceBackMaterial.shader = surfaceShader;
				VolumeMaterial.shader = volumeShader;
				VolumeBackMaterial.shader = volumeShader;

				UpdateSurfaceMaterial();
				UpdateSurfaceBackMaterial();
				UpdateVolumeMaterials();
			}
		}

		public void UpdateSurfaceMaterial()
		{
			var qualityLevel = WaterQualitySettings.Instance.CurrentQualityLevel;

			SurfaceMaterial.SetFloat(cullId, 2);

			float maxTesselationFactor = Mathf.Sqrt(2000000.0f / Mathf.Min(water.Geometry.TesselatedBaseVertexCount, WaterQualitySettings.Instance.MaxTesselatedVertexCount));
			water.Renderer.PropertyBlock.SetFloat("_TesselationFactor", Mathf.Lerp(1.0f, maxTesselationFactor, Mathf.Min(tesselationFactor, qualityLevel.maxTesselationFactor)));

			if(!Application.isPlaying)
			{
				bool alphaBlend = water.ShaderSet.TransparencyMode == WaterTransparencyMode.Refractive && qualityLevel.allowAlphaBlending;

				if(Camera.main != null)
				{
					var mainWaterCamera = Camera.main.GetComponent<WaterCamera>();

					if(mainWaterCamera == null)
						mainWaterCamera = UnityEngine.Object.FindObjectOfType<WaterCamera>();

					if(mainWaterCamera != null && mainWaterCamera.RenderMode < WaterRenderMode.ImageEffectDeferred)
						SetBlendMode(alphaBlend);
					else
						SetBlendMode(false);
				}
				else
					SetBlendMode(false);
			}

			water.Renderer.PropertyBlock.SetFloat(parameterHashes[23], -1.0f);            // _RefractionMaxDepth

			if(alphaBlendMode != 0)
				SetBlendMode(alphaBlendMode == 2);

			// set keywords
			string shaderName = SurfaceMaterial.shader.name;

			if(shaderName.Contains("_WAVES_FFT"))
				SurfaceMaterial.EnableKeyword("_WAVES_FFT");

			if (shaderName.Contains("_BOUNDED_WATER"))
				SurfaceMaterial.EnableKeyword("_BOUNDED_WATER");

			if (shaderName.Contains("_WAVES_ALIGN"))
				SurfaceMaterial.EnableKeyword("_WAVES_ALIGN");

			if(shaderName.Contains("_WAVES_GERSTNER"))
				SurfaceMaterial.EnableKeyword("_WAVES_GERSTNER");

			if (water.Geometry.Triangular)
				SurfaceMaterial.EnableKeyword("_TRIANGLES");
		}

		private void OnProfilesChanged(Water water)
		{
			var profiles = water.ProfilesManager.Profiles;
			var topProfile = profiles[0].Profile;
			float topWeight = 0.0f;

			for(int i = 0; i < profiles.Length; ++i)
			{
				var weightedProfile = profiles[i];

				if(topProfile == null || topWeight < weightedProfile.Weight)
				{
					topProfile = weightedProfile.Profile;
					topWeight = weightedProfile.Weight;
				}
			}

			horizontalDisplacementScale = 0.0f;
			underwaterBlurSize = 0.0f;
			underwaterLightFadeScale = 0.0f;
			underwaterDistortionsIntensity = 0.0f;
			underwaterDistortionAnimationSpeed = 0.0f;

			Color absorptionColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color diffuseColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color specularColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color depthColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color emissionColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color reflectionColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color foamDiffuseColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color foamSpecularColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			Color subsurfaceScatteringShoreColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);

			smoothness = 0.0f;
			ambientSmoothness = 0.0f;
			float refractionDistortion = 0.0f;
			float detailFadeDistance = 0.0f;
			float displacementNormalsIntensity = 0.0f;
			float edgeBlendFactor = 0.0f;
			float directionalWrapSSS = 0.0f;
			float pointWrapSSS = 0.0f;
			forwardScatteringIntensity = 0.0f;

			Vector3 planarReflectionPack = new Vector3();
			Vector2 foamTiling = new Vector2();
			var normalMapAnimation1 = new NormalMapAnimation();
			var normalMapAnimation2 = new NormalMapAnimation();

			for(int i = 0; i < profiles.Length; ++i)
			{
				var profile = profiles[i].Profile;
				float weight = profiles[i].Weight;

				horizontalDisplacementScale += profile.HorizontalDisplacementScale * weight;
				underwaterBlurSize += profile.UnderwaterBlurSize * weight;
				underwaterLightFadeScale += profile.UnderwaterLightFadeScale*weight;
				underwaterDistortionsIntensity += profile.UnderwaterDistortionsIntensity * weight;
				underwaterDistortionAnimationSpeed += profile.UnderwaterDistortionAnimationSpeed * weight;

				absorptionColor += profile.AbsorptionColor * weight;
				diffuseColor += profile.DiffuseColor * weight;
				specularColor += profile.SpecularColor * weight;
				depthColor += profile.DepthColor * weight;
				emissionColor += profile.EmissionColor * weight;
				reflectionColor += profile.ReflectionColor * weight;
				foamDiffuseColor += profile.FoamDiffuseColor * weight;
				foamSpecularColor += profile.FoamSpecularColor * weight;

				smoothness += profile.Smoothness * weight;
				ambientSmoothness += profile.AmbientSmoothness * weight;
				refractionDistortion += profile.RefractionDistortion * weight;
				subsurfaceScatteringShoreColor += profile.SubsurfaceScatteringShoreColor * weight;
				detailFadeDistance += profile.DetailFadeDistance * weight;
				displacementNormalsIntensity += profile.DisplacementNormalsIntensity * weight;
				edgeBlendFactor += profile.EdgeBlendFactor * weight;
				directionalWrapSSS += profile.DirectionalWrapSSS * weight;
				pointWrapSSS += profile.PointWrapSSS * weight;
				forwardScatteringIntensity += profile.ForwardScatteringIntensity*weight;

				planarReflectionPack.x += profile.PlanarReflectionIntensity * weight;
				planarReflectionPack.y += profile.PlanarReflectionFlatten * weight;
				planarReflectionPack.z += profile.PlanarReflectionVerticalOffset * weight;

				foamTiling += profile.FoamTiling * weight;
				normalMapAnimation1 += profile.NormalMapAnimation1 * weight;
				normalMapAnimation2 += profile.NormalMapAnimation2 * weight;
			}

			if(water.WindWaves != null && water.WindWaves.FinalRenderMode == WaveSpectrumRenderMode.GerstnerAndFFTNormals)
				displacementNormalsIntensity *= 0.5f;

			var block = water.Renderer.PropertyBlock;

			subsurfaceScatteringShoreColor.a = forwardScatteringIntensity;

			// apply to materials
			block.SetVector(parameterHashes[0], absorptionColor);                    // _AbsorptionColor
			block.SetColor(parameterHashes[1], diffuseColor);                       // _Color
			block.SetColor(parameterHashes[2], specularColor);                      // _SpecColor
			block.SetColor(parameterHashes[3], depthColor);                         // _DepthColor
			block.SetColor(parameterHashes[4], emissionColor);                      // _EmissionColor
			block.SetColor(parameterHashes[5], reflectionColor);                    // _ReflectionColor
			block.SetColor(parameterHashes[24], foamDiffuseColor);                 // _FoamDiffuseColor
			block.SetColor(parameterHashes[22], foamSpecularColor);                 // _FoamSpecularColor
			block.SetFloat(parameterHashes[6], horizontalDisplacementScale);        // _DisplacementsScale

			block.SetFloat(parameterHashes[7], ambientSmoothness);                         // _Glossiness
			block.SetVector(parameterHashes[9], new Vector4(directionalWrapSSS, 1.0f / (1.0f + directionalWrapSSS), pointWrapSSS, 1.0f / (1.0f + pointWrapSSS)));           // _WrapSubsurfaceScatteringPack
			block.SetFloat(parameterHashes[10], refractionDistortion);               // _RefractionDistortion
			block.SetColor(parameterHashes[11], subsurfaceScatteringShoreColor);     // _SubsurfaceScatteringShoreColor
			block.SetFloat(parameterHashes[12], 1.0f / detailFadeDistance);			// _DetailFadeFactor
			block.SetFloat(parameterHashes[13], displacementNormalsIntensity);      // _DisplacementNormalsIntensity
			block.SetFloat(parameterHashes[14], 1.0f / edgeBlendFactor);            // _EdgeBlendFactorInv
			block.SetVector(parameterHashes[15], planarReflectionPack);             // _PlanarReflectionPack
			block.SetVector(parameterHashes[16], new Vector4(normalMapAnimation1.Intensity, normalMapAnimation2.Intensity, -(normalMapAnimation1.Intensity + normalMapAnimation2.Intensity) * 0.5f, 0.0f));             // _BumpScale
			block.SetVector(parameterHashes[17], new Vector2(foamTiling.x / normalMapAnimation1.Tiling.x, foamTiling.y / normalMapAnimation1.Tiling.y));                    // _FoamTiling
			block.SetFloat(parameterHashes[18], smoothness / ambientSmoothness);    // _LightSmoothnessMul

			SurfaceMaterial.SetTexture(parameterHashes[19], topProfile.NormalMap);            // _BumpMap
			SurfaceMaterial.SetTexture(parameterHashes[20], topProfile.FoamDiffuseMap);       // _FoamTex
			SurfaceMaterial.SetTexture(parameterHashes[21], topProfile.FoamNormalMap);        // _FoamNormalMap

			water.UVAnimator.NormalMapAnimation1 = normalMapAnimation1;
			water.UVAnimator.NormalMapAnimation2 = normalMapAnimation2;

			if(vectorOverrides != null)
				ApplyOverridenParameters();

			absorptionGradientDirty = true;

			UpdateSurfaceBackMaterial();
			UpdateVolumeMaterials();
			UpdateGlobalLookupTex();
		}

		private void UpdateSurfaceBackMaterial()
		{
			if(SurfaceBackMaterial == null)
				return;

			if(SurfaceBackMaterial.shader != SurfaceMaterial.shader)
				SurfaceBackMaterial.shader = SurfaceMaterial.shader;

			Color specularBackColor = SurfaceBackMaterial.GetColor(parameterHashes[2]);
			SurfaceBackMaterial.CopyPropertiesFromMaterial(SurfaceMaterial);
			SurfaceBackMaterial.SetColor(parameterHashes[2], specularBackColor);
			SurfaceBackMaterial.SetFloat(parameterHashes[11], 0.0f); // air has IOR of 1.0, fresnel bias should be 0.0
			SurfaceBackMaterial.EnableKeyword("_WATER_BACK");
			SurfaceBackMaterial.SetFloat(cullId, 1);
		}

		private void UpdateVolumeMaterials()
		{
			if(VolumeMaterial == null)
				return;

			VolumeMaterial.CopyPropertiesFromMaterial(SurfaceMaterial);
			VolumeBackMaterial.CopyPropertiesFromMaterial(SurfaceMaterial);

			if(SurfaceMaterial.renderQueue == 2990)
				VolumeBackMaterial.renderQueue = VolumeMaterial.renderQueue = 2991;

			VolumeBackMaterial.SetFloat(cullId, 1);
		}

		private void ApplyOverridenParameters()
		{
			var block = water.Renderer.PropertyBlock;

			for(int i = 0; i < vectorOverrides.Length; ++i)
				block.SetVector(vectorOverrides[i].Hash, vectorOverrides[i].Value);

			for(int i = 0; i < floatOverrides.Length; ++i)
				block.SetFloat(floatOverrides[i].Hash, floatOverrides[i].Value);
		}

		private void OnWaterIdChanged()
		{
			int waterId = water.WaterId;

			water.Renderer.PropertyBlock.SetVector(waterShaderId, new Vector4(1 << waterId, 1 << (waterId + 1), (waterId + 0.5f) / 256.0f, 0));
		}

		private static void RemoveArrayElementAt<T>(ref T[] array, int index)
		{
			var originalArray = array;
			var newArray = new T[array.Length - 1];

			for(int i = 0; i < index; ++i)
				newArray[i] = originalArray[i];

			for(int i = index; i < newArray.Length; ++i)
				newArray[i] = originalArray[i + 1];

			array = newArray;
		}

		private static void CreateParameterHashes()
		{
			if(parameterHashes != null && parameterHashes.Length == parameterNames.Length)
				return;

			int numParameters = parameterNames.Length;
			parameterHashes = new int[numParameters];

			for(int i = 0; i < numParameters; ++i)
				parameterHashes[i] = Shader.PropertyToID(parameterNames[i]);

			surfaceOffsetId = Shader.PropertyToID("_SurfaceOffset");
			waterShaderId = Shader.PropertyToID("_WaterId");
			cullId = Shader.PropertyToID("_Cull");
		}

		public void UpdateGlobalLookupTex()
		{
			int waterId = water.WaterId;

			if(globalWaterLookupTex == null || waterId == -1)
				return;

			if(waterId >= 256)
			{
				Debug.LogError("There is more than 256 water objects in the scene. This is not supported in deferred water render mode. You have to switch to WaterCameraSimple.");
				return;
			}

			var absorptionColor = GetParameterValue(ColorParameter.AbsorptionColor);
			absorptionColor.a = water.Renderer.PropertyBlock.GetFloat(parameterHashes[10]);                 // _RefractionDistortion
			globalWaterLookupTex.SetPixel(waterId, 0, absorptionColor);

			var reflectionColor = GetParameterValue(ColorParameter.ReflectionColor);                        // alpha channel is free here
			reflectionColor.a = forwardScatteringIntensity;
			globalWaterLookupTex.SetPixel(waterId, 1, reflectionColor);

			var surfaceOffset = water.SurfaceOffset;
			globalWaterLookupTex.SetPixel(waterId, 2, new Color(surfaceOffset.x, water.transform.position.y, surfaceOffset.y, directionalLightsIntensity));

			var diffuseColor = GetParameterValue(ColorParameter.DiffuseColor);                              // alpha channel is free here
			diffuseColor.a = smoothness / ambientSmoothness;
			globalWaterLookupTex.SetPixel(waterId, 3, diffuseColor);

			globalLookupDirty = true;
		}

		private void UpdateGlobalLookupTexOffset()
		{
			int waterId = water.WaterId;

			if(globalWaterLookupTex == null || waterId == -1)
				return;

			if(waterId >= 256)
			{
				Debug.LogError("There is more than 256 water objects in the scene. This is not supported in deferred water render mode. You have to switch to WaterCameraSimple.");
				return;
			}

			var surfaceOffset = water.SurfaceOffset;
			globalWaterLookupTex.SetPixel(waterId, 2, new Color(surfaceOffset.x, water.transform.position.y, surfaceOffset.y, directionalLightsIntensity));

			globalLookupDirty = true;
		}

		public static void ValidateGlobalWaterDataLookupTex()
		{
			if(globalWaterLookupTex == null)
			{
				CreateGlobalWaterDataLookupTex();
				globalLookupDirty = false;
			}
			else if(globalLookupDirty)
			{
				globalWaterLookupTex.Apply(false, false);
				globalLookupDirty = false;

				Shader.SetGlobalTexture("_GlobalWaterLookupTex", globalWaterLookupTex);
			}
		}

		private static void CreateGlobalWaterDataLookupTex()
		{
			globalWaterLookupTex = new Texture2D(256, 4, TextureFormat.RGBAHalf, false, true)
			{
				hideFlags = HideFlags.DontSave,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp
			};

			Color clearColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

			for(int i = 0; i < 256; ++i)
			{
				globalWaterLookupTex.SetPixel(i, 0, clearColor);
				globalWaterLookupTex.SetPixel(i, 1, clearColor);
				globalWaterLookupTex.SetPixel(i, 2, clearColor);
				globalWaterLookupTex.SetPixel(i, 3, clearColor);
			}

			var waters = WaterGlobals.Instance.Waters;
			int numWaters = waters.Count;

			for(int i = 0; i < numWaters; ++i)
				waters[i].Materials.UpdateGlobalLookupTex();

			globalWaterLookupTex.Apply(false, false);

			Shader.SetGlobalTexture("_GlobalWaterLookupTex", globalWaterLookupTex);
		}

		private void ComputeAbsorptionGradient()
		{
			absorptionGradientDirty = false;

			if(absorptionGradient == null)
				absorptionGradient = new Texture2D(GradientResolution, 1, TextureFormat.RGBAHalf, false, true) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

			var profiles = water.ProfilesManager.Profiles;

			for (int pixelIndex = 0; pixelIndex < GradientResolution; ++pixelIndex)
				absorptionColorsBuffer[pixelIndex] = new Color(0.0f, 0.0f, 0.0f, 0.0f);

			for(int profileIndex = 0; profileIndex < profiles.Length; ++profileIndex)
			{
				var gradient = profiles[profileIndex].Profile.AbsorptionColorByDepth;
				float weight = profiles[profileIndex].Weight;

				for (int pixelIndex = 0; pixelIndex < GradientResolution; ++pixelIndex)
					absorptionColorsBuffer[pixelIndex] += gradient.Evaluate(pixelIndex / 31.0f) * weight;
			}

			absorptionGradient.SetPixels(absorptionColorsBuffer);
			absorptionGradient.Apply();
		}

		public class WaterParameterFloat
		{
			public readonly int Hash;

			private readonly MaterialPropertyBlock propertyBlock;
			private float value;

			public WaterParameterFloat(MaterialPropertyBlock propertyBlock, int hash, float value)
			{
				this.propertyBlock = propertyBlock;
				this.Hash = hash;
				this.value = value;
			}

			public float Value
			{
				get { return value; }
				set
				{
					this.value = value;
					propertyBlock.SetFloat(Hash, value);
				}
			}
		}

		public class WaterParameterVector4
		{
			public readonly int Hash;

			private readonly MaterialPropertyBlock propertyBlock;
			private Vector4 value;

			public WaterParameterVector4(MaterialPropertyBlock propertyBlock, int hash, Vector4 value)
			{
				this.propertyBlock = propertyBlock;
				this.Hash = hash;
				this.value = value;
			}

			public Vector4 Value
			{
				get { return value; }
				set
				{
					this.value = value;
					propertyBlock.SetVector(Hash, value);
				}
			}
		}
	}
}
