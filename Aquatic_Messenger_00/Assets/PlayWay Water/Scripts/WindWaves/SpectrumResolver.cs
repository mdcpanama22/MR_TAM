using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Renders all types of water spectra and animates them in time on CPU and GPU. This class in hierarchy contains GPU
	///     code.
	/// </summary>
	public class SpectrumResolver : SpectrumResolverCPU
	{
		private Texture2D tileSizeLookup;           // 2x2 tile sizes tex
		private Texture omnidirectionalSpectrum;
		private RenderTexture totalOmnidirectionalSpectrum;
		private RenderTexture directionalSpectrum;
		private RenderTexture heightSpectrum, normalSpectrum, displacementSpectrum;
		private RenderBuffer[] renderTargetsx2;
		private RenderBuffer[] renderTargetsx3;

		private bool tileSizesLookupDirty = true;
		private bool directionalSpectrumDirty = true;
		private Vector4 tileSizes;
		private Mesh spectrumDownsamplingMesh;

		private readonly int renderTimeId;
		private readonly Material animationMaterial;
		private readonly Water water;
		private readonly WindWaves windWaves;

		public SpectrumResolver(Water water, WindWaves windWaves, Shader spectrumShader) : base(water, windWaves, 4)
		{
			this.water = water;
			this.windWaves = windWaves;

			renderTimeId = Shader.PropertyToID("_RenderTime");

			animationMaterial = new Material(spectrumShader) {hideFlags = HideFlags.DontSave};
			animationMaterial.SetFloat(renderTimeId, Time.time);

			if (windWaves.LoopDuration != 0.0f)
			{
				animationMaterial.EnableKeyword("_LOOPING");
				animationMaterial.SetFloat("_LoopDuration", windWaves.LoopDuration);
			}
		}

		public Texture TileSizeLookup
		{
			get
			{
				ValidateTileSizeLookup();
				return tileSizeLookup;
			}
		}
		
		public float RenderTime { get; private set; }

		public Texture RenderHeightSpectrumAt(float time)
		{
			CheckResources();

			var directionalSpectrum = GetRawDirectionalSpectrum();

			RenderTime = time;
			animationMaterial.SetFloat(renderTimeId, time);
			Graphics.Blit(directionalSpectrum, heightSpectrum, animationMaterial, 0);

			return heightSpectrum;
		}

		public Texture RenderNormalsSpectrumAt(float time)
		{
			CheckResources();

			var directionalSpectrum = GetRawDirectionalSpectrum();

			RenderTime = time;
			animationMaterial.SetFloat(renderTimeId, time);
			Graphics.Blit(directionalSpectrum, normalSpectrum, animationMaterial, 1);

			return normalSpectrum;
		}

		public void RenderDisplacementsSpectraAt(float time, out Texture height, out Texture displacement)
		{
			CheckResources();

			height = heightSpectrum;
			displacement = displacementSpectrum;

			// it's necessary to set it each frame for some reason
			renderTargetsx2[0] = heightSpectrum.colorBuffer;
			renderTargetsx2[1] = displacementSpectrum.colorBuffer;

			var directionalSpectrum = GetRawDirectionalSpectrum();

			RenderTime = time;
			animationMaterial.SetFloat(renderTimeId, time);
			Graphics.SetRenderTarget(renderTargetsx2, heightSpectrum.depthBuffer);
			Graphics.Blit(directionalSpectrum, animationMaterial, 5);
			Graphics.SetRenderTarget(null);
		}

		public void RenderCompleteSpectraAt(float time, out Texture height, out Texture normal, out Texture displacement)
		{
			CheckResources();

			height = heightSpectrum;
			normal = normalSpectrum;
			displacement = displacementSpectrum;

			// it's necessary to set it each frame for some reason
			renderTargetsx3[0] = heightSpectrum.colorBuffer;
			renderTargetsx3[1] = normalSpectrum.colorBuffer;
			renderTargetsx3[2] = displacementSpectrum.colorBuffer;

			var directionalSpectrum = GetRawDirectionalSpectrum();

			RenderTime = time;
			animationMaterial.SetFloat(renderTimeId, time);
			Graphics.SetRenderTarget(renderTargetsx3, heightSpectrum.depthBuffer);
			Graphics.Blit(directionalSpectrum, animationMaterial, 2);
			Graphics.SetRenderTarget(null);
		}

		public Texture GetSpectrum(SpectrumType type)
		{
			switch(type)
			{
				case SpectrumType.Height: return heightSpectrum;
				case SpectrumType.Normal: return normalSpectrum;
				case SpectrumType.Displacement: return displacementSpectrum;
				case SpectrumType.RawDirectional: return directionalSpectrum;
				case SpectrumType.RawOmnidirectional: return omnidirectionalSpectrum;
				default: throw new System.InvalidOperationException();
			}
		}

		public override void AddSpectrum(WaterWavesSpectrumDataBase spectrum)
		{
			base.AddSpectrum(spectrum);
			directionalSpectrumDirty = true;
		}

		public override void RemoveSpectrum(WaterWavesSpectrumDataBase spectrum)
		{
			base.RemoveSpectrum(spectrum);
			directionalSpectrumDirty = true;
		}

		internal override void OnProfilesChanged()
		{
			base.OnProfilesChanged();

			if(tileSizes != windWaves.TileSizes)
			{
				tileSizesLookupDirty = true;
				tileSizes = windWaves.TileSizes;
			}

			RenderTotalOmnidirectionalSpectrum();
		}

		private void RenderTotalOmnidirectionalSpectrum()
		{
			animationMaterial.SetFloat("_Gravity", water.Gravity);
			animationMaterial.SetVector("_TargetResolution", new Vector4(windWaves.FinalResolution, windWaves.FinalResolution, 0.0f, 0.0f));

			var profiles = water.ProfilesManager.Profiles;

			if(profiles.Length > 1)
			{
				var totalOmnidirectionalSpectrum = GetTotalOmnidirectionalSpectrum();

				Graphics.SetRenderTarget(totalOmnidirectionalSpectrum);
				GL.Clear(false, true, Color.black);
				Graphics.SetRenderTarget(null);

				for(int i=0; i<profiles.Length; ++i)
				{
					var weightedProfile = profiles[i];

					if(weightedProfile.Weight <= 0.0001f)
						continue;

					var spectrum = weightedProfile.Profile.Spectrum;

					WaterWavesSpectrumData spectrumData;

					if(!spectraDataCache.TryGetValue(spectrum, out spectrumData))
						spectrumData = GetSpectrumData(spectrum);

					animationMaterial.SetFloat("_Weight", spectrumData.Weight);
					Graphics.Blit(spectrumData.Texture, totalOmnidirectionalSpectrum, animationMaterial, 4);
				}

				omnidirectionalSpectrum = totalOmnidirectionalSpectrum;
			}
			else if(profiles.Length != 0)
			{
				var spectrum = profiles[0].Profile.Spectrum;
				WaterWavesSpectrumData spectrumData;

				if(!spectraDataCache.TryGetValue(spectrum, out spectrumData))
					spectrumData = GetSpectrumData(spectrum);

				spectrumData.Weight = 1.0f;
				omnidirectionalSpectrum = spectrumData.Texture;
			}
			
			water.Renderer.PropertyBlock.SetFloat("_MaxDisplacement", MaxHorizontalDisplacement);
		}

		public override void SetDirectionalSpectrumDirty()
		{
			base.SetDirectionalSpectrumDirty();

			directionalSpectrumDirty = true;
		}

		private void RenderDirectionalSpectrum()
		{
			if(omnidirectionalSpectrum == null)
				RenderTotalOmnidirectionalSpectrum();

			ValidateTileSizeLookup();

			animationMaterial.SetFloat("_Directionality", 1.0f - windWaves.SpectrumDirectionality);
			animationMaterial.SetVector("_WindDirection", WindDirection);
			animationMaterial.SetTexture("_TileSizeLookup", tileSizeLookup);
			Graphics.Blit(omnidirectionalSpectrum, directionalSpectrum, animationMaterial, 3);

			AddOverlayToDirectionalSpectrum();

			directionalSpectrumDirty = false;
		}

		private void AddOverlayToDirectionalSpectrum()
		{
			if (spectrumDownsamplingMesh == null)
				spectrumDownsamplingMesh = CreateDownsamplingMesh();

			for (int i = overlayedSpectra.Count - 1; i >= 0; --i)
			{
				var spectrumData = overlayedSpectra[i];
				var texture = spectrumData.Texture;

				animationMaterial.SetFloat("_Weight", spectrumData.Weight);
				animationMaterial.SetVector("_WindDirection", spectrumData.WindDirection);
				
				float radius = spectrumData.WeatherSystemRadius;
				animationMaterial.SetVector("_WeatherSystemRadius", new Vector4(2.0f * radius, radius * radius, 0.0f, 0.0f));

				Vector2 offset = spectrumData.WeatherSystemOffset;
				animationMaterial.SetVector("_WeatherSystemOffset", new Vector4(offset.x, offset.y, offset.magnitude, 0.0f));

				Graphics.Blit(texture, directionalSpectrum, animationMaterial, 6);

				/*animationMaterial.mainTexture = texture;
				animationMaterial.SetFloat("_ResolutionRatio", (float)texture.width/directionalSpectrum.width);
				GL.PushMatrix();
				GL.modelview = Matrix4x4.identity;
				GL.LoadProjectionMatrix(Matrix4x4.identity);

				if (animationMaterial.SetPass(6))
				{
					Graphics.SetRenderTarget(directionalSpectrum);
					Graphics.DrawMeshNow(spectrumDownsamplingMesh, Matrix4x4.identity);
				}

				GL.PopMatrix();*/
			}

			//Graphics.SetRenderTarget(null);
			//animationMaterial.mainTexture = null;
		}

		internal RenderTexture GetRawDirectionalSpectrum()
		{
			if((directionalSpectrumDirty || !directionalSpectrum.IsCreated()) && Application.isPlaying)
			{
				CheckResources();
				RenderDirectionalSpectrum();
			}

			return directionalSpectrum;
		}

		private RenderTexture GetTotalOmnidirectionalSpectrum()
		{
			if(totalOmnidirectionalSpectrum == null)
			{
				int finalResolutionx2 = windWaves.FinalResolution << 1;

				totalOmnidirectionalSpectrum = new RenderTexture(finalResolutionx2, finalResolutionx2, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
				{
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Repeat
				};
			}

			return totalOmnidirectionalSpectrum;
		}
		
		private void CheckResources()
		{
			if(heightSpectrum == null)          // these are always all null or non-null
			{
				int finalResolutionx2 = windWaves.FinalResolution << 1;
				bool highPrecision = windWaves.FinalHighPrecision;

				heightSpectrum = new RenderTexture(finalResolutionx2, finalResolutionx2, 0, highPrecision ? RenderTextureFormat.RGFloat : RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
				{
					name = "Water Height Spectrum",
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Point
				};

				normalSpectrum = new RenderTexture(finalResolutionx2, finalResolutionx2, 0, highPrecision ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
				{
					name = "Water Normals Spectrum",
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Point
				};

				displacementSpectrum = new RenderTexture(finalResolutionx2, finalResolutionx2, 0, highPrecision ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
				{
					name = "Water Displacement Spectrum",
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Point
				};

				directionalSpectrum = new RenderTexture(finalResolutionx2, finalResolutionx2, 0, highPrecision ? RenderTextureFormat.RGFloat : RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
				{
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};

				renderTargetsx2 = new[] { heightSpectrum.colorBuffer, displacementSpectrum.colorBuffer };
				renderTargetsx3 = new[] { heightSpectrum.colorBuffer, normalSpectrum.colorBuffer, displacementSpectrum.colorBuffer };
			}
		}

		internal override void OnMapsFormatChanged(bool resolution)
		{
			base.OnMapsFormatChanged(resolution);

			if(totalOmnidirectionalSpectrum != null)
			{
				Object.Destroy(totalOmnidirectionalSpectrum);
				totalOmnidirectionalSpectrum = null;
			}

			if(heightSpectrum != null)
			{
				Object.Destroy(heightSpectrum);
				heightSpectrum = null;
			}

			if(normalSpectrum != null)
			{
				Object.Destroy(normalSpectrum);
				normalSpectrum = null;
			}

			if(displacementSpectrum != null)
			{
				Object.Destroy(displacementSpectrum);
				displacementSpectrum = null;
			}

			if(directionalSpectrum != null)
			{
				Object.Destroy(directionalSpectrum);
				directionalSpectrum = null;
			}

			if (tileSizeLookup != null)
			{
				Object.Destroy(tileSizeLookup);
				tileSizeLookup = null;
				tileSizesLookupDirty = true;
			}
			
			omnidirectionalSpectrum = null;
			renderTargetsx2 = null;
			renderTargetsx3 = null;
		}

		private void ValidateTileSizeLookup()
		{
			if(tileSizesLookupDirty)
			{
				if(tileSizeLookup == null)
				{
					tileSizeLookup = new Texture2D(2, 2, SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat) ? TextureFormat.RGBAFloat : TextureFormat.RGBAHalf, false, true)
					{
						hideFlags = HideFlags.DontSave,
						wrapMode = TextureWrapMode.Clamp,
						filterMode = FilterMode.Point
					};
				}

				tileSizeLookup.SetPixel(0, 0, new Color(0.5f, 0.5f, 1.0f / tileSizes.x, 0.0f));
				tileSizeLookup.SetPixel(1, 0, new Color(1.5f, 0.5f, 1.0f / tileSizes.y, 0.0f));
				tileSizeLookup.SetPixel(0, 1, new Color(0.5f, 1.5f, 1.0f / tileSizes.z, 0.0f));
				tileSizeLookup.SetPixel(1, 1, new Color(1.5f, 1.5f, 1.0f / tileSizes.w, 0.0f));
				tileSizeLookup.Apply(false, false);

				tileSizesLookupDirty = false;
			}
		}

		private static void AddQuad(Vector3[] vertices, Vector3[] origins, Vector2[] uvs, int index, float xOffset, float yOffset, int originIndex)
		{
			originIndex += index;

			float xOffsetV = xOffset * 2.0f - 1.0f;
			float yOffsetV = yOffset * 2.0f - 1.0f;

			uvs[index] = new Vector2(xOffset, yOffset);
			vertices[index++] = new Vector3(xOffsetV, yOffsetV, 0.1f);

			uvs[index] = new Vector2(xOffset, yOffset + 0.25f);
			vertices[index++] = new Vector3(xOffsetV, yOffsetV + 0.5f, 0.1f);

			uvs[index] = new Vector2(xOffset + 0.25f, yOffset + 0.25f);
			vertices[index++] = new Vector3(xOffsetV + 0.5f, yOffsetV + 0.5f, 0.1f);

			uvs[index] = new Vector2(xOffset + 0.25f, yOffset);
			vertices[index] = new Vector3(xOffsetV + 0.5f, yOffsetV, 0.1f);

			origins[index--] = vertices[originIndex];
			origins[index--] = vertices[originIndex];
			origins[index--] = vertices[originIndex];
			origins[index] = vertices[originIndex];
		}

		private static void AddQuads(Vector3[] vertices, Vector3[] origins, Vector2[] uvs, int index, float xOffset, float yOffset)
		{
			AddQuad(vertices, origins, uvs, index, xOffset, yOffset, 0);
			AddQuad(vertices, origins, uvs, index + 4, xOffset + 0.25f, yOffset, 3);
			AddQuad(vertices, origins, uvs, index + 8, xOffset, yOffset + 0.25f, 1);
			AddQuad(vertices, origins, uvs, index + 12, xOffset + 0.25f, yOffset + 0.25f, 2);
		}

		private static Mesh CreateDownsamplingMesh()
		{
			var mesh = new Mesh { name = "[PW Water] Spectrum Downsampling Mesh" };

			var vertices = new Vector3[64];
			var origins = new Vector3[64];
			var uvs = new Vector2[64];
			var indices = new int[64];

			for(int i = 0; i < indices.Length; ++i)
				indices[i] = i;

			AddQuads(vertices, origins, uvs, 0, 0.0f, 0.0f);        // tile 0
			AddQuads(vertices, origins, uvs, 16, 0.5f, 0.0f);        // tile 1
			AddQuads(vertices, origins, uvs, 32, 0.0f, 0.5f);        // tile 2
			AddQuads(vertices, origins, uvs, 48, 0.5f, 0.5f);        // tile 3

			mesh.vertices = vertices;
			mesh.normals = origins;
			mesh.uv = uvs;
			mesh.SetIndices(indices, MeshTopology.Quads, 0);

			return mesh;
		}

		public enum SpectrumType
		{
			Height,
			Normal,
			Displacement,
			RawDirectional,
			RawOmnidirectional
		}
	}
}
