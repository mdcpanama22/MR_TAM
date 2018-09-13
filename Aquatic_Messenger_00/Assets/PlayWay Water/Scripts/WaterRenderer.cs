using System.Collections.Generic;
using PlayWay.Water.Internal;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	/// <summary>
	///     Renders water.
	///     <seealso cref="Water.Renderer" />
	/// </summary>
	[System.Serializable]
	public class WaterRenderer
	{
		[HideInInspector][SerializeField] private Shader volumeFrontShader;
		[HideInInspector][SerializeField] private Shader volumeFrontFastShader;
		[HideInInspector][SerializeField] private Shader volumeBackShader;

		[SerializeField] private Transform reflectionProbeAnchor;
		[SerializeField] private ShadowCastingMode shadowCastingMode;
		[SerializeField] private bool useSharedMask = true;
		
        private Water water;
		private MaterialPropertyBlock propertyBlock;
		private RenderTexture additiveMaskTexture;
		private RenderTexture subtractiveMaskTexture;
		private readonly List<Renderer> masks = new List<Renderer>();
		
		private static int additiveMaskId, subtractiveMaskId;

		internal void Start(Water water)
		{
			this.water = water;

			additiveMaskId = Shader.PropertyToID("_AdditiveMask");
			subtractiveMaskId = Shader.PropertyToID("_SubtractiveMask");
		}

		internal void Enable()
		{

		}

		internal void Disable()
		{

        }

		public int MaskCount
		{
			get { return masks.Count; }
		}

		public MaterialPropertyBlock PropertyBlock
		{
			get { return propertyBlock != null ? propertyBlock : (propertyBlock = new MaterialPropertyBlock()); }
		}

		public Transform ReflectionProbeAnchor
		{
			get { return reflectionProbeAnchor; }
			set { reflectionProbeAnchor = value; }
		}

		public void AddMask(Renderer mask)
		{
			mask.enabled = false;
			masks.Add(mask);
		}

		public void RemoveMask(Renderer mask)
		{
			masks.Remove(mask);
		}
		
		internal void Validate()
		{
			if(volumeFrontShader == null)
				volumeFrontShader = Shader.Find("PlayWay Water/Volumes/Front");

			if(volumeFrontFastShader == null)
				volumeFrontFastShader = Shader.Find("PlayWay Water/Volumes/Front Simple");

			if(volumeBackShader == null)
				volumeBackShader = Shader.Find("PlayWay Water/Volumes/Back");
		}

		public void RenderEffects(Camera camera)
		{
			if(!water.isActiveAndEnabled || (camera.cullingMask & (1 << water.gameObject.layer)) == 0)
				return;

			var waterCamera = WaterCamera.GetWaterCamera(camera);
			bool hasWaterCamera = (object)waterCamera != null;

			if(!hasWaterCamera || (!water.Volume.Boundless && water.Volume.HasRenderableAdditiveVolumes && !waterCamera.RenderVolumes))
				return;

			water.OnWaterRender(camera);
		}

		public void Render(Camera camera, WaterGeometryType geometryType, CommandBuffer commandBuffer = null, Shader shader = null)
		{
			if(!water.isActiveAndEnabled || (camera.cullingMask & (1 << water.gameObject.layer)) == 0)
				return;

			var waterCamera = WaterCamera.GetWaterCamera(camera);
			bool hasWaterCamera = (object)waterCamera != null;

			if(!hasWaterCamera || (!water.Volume.Boundless && water.Volume.HasRenderableAdditiveVolumes && !waterCamera.RenderVolumes))
				return;
			
			if(water.ShaderSet.ReceiveShadows)
			{
				Vector2 min = new Vector2(0.0f, 0.0f);
				Vector2 max = new Vector2(1.0f, 1.0f);
				waterCamera.ReportShadowedWaterMinMaxRect(min, max);
            }

			if(!useSharedMask)
				RenderMasks(camera, waterCamera, propertyBlock);

			Matrix4x4 matrix;
			var meshes = water.Geometry.GetTransformedMeshes(camera, out matrix, geometryType, false, waterCamera.ForcedVertexCount);
			
			if (commandBuffer == null)
			{
				Camera finalCamera = waterCamera.RenderMode != WaterRenderMode.DefaultQueue ? waterCamera.WaterRenderCamera : camera;

				for (int i = 0; i < meshes.Length; ++i)
				{
					Graphics.DrawMesh(meshes[i], matrix, water.Materials.SurfaceMaterial, water.gameObject.layer, finalCamera, 0,
						propertyBlock, shadowCastingMode, false, reflectionProbeAnchor != null ? reflectionProbeAnchor : water.transform,
						false);

					if (waterCamera.ContainingWater != null && waterCamera.Type == WaterCamera.CameraType.Normal)
					{
						Graphics.DrawMesh(meshes[i], matrix, water.Materials.SurfaceBackMaterial, water.gameObject.layer, finalCamera, 0,
							propertyBlock, shadowCastingMode, false, reflectionProbeAnchor != null ? reflectionProbeAnchor : water.transform,
							false);
					}
				}
			}
			else
			{
				var material = UtilityShaderVariants.Instance.GetVariant(shader, water.Materials.UsedKeywords);

				for(int i = 0; i < meshes.Length; ++i)
					commandBuffer.DrawMesh(meshes[i], matrix, material, 0, 0, propertyBlock);
			}
		}

		public void RenderVolumes(CommandBuffer commandBuffer, Shader shader, bool twoPass)
		{
			if(!water.enabled) return;

			var material = UtilityShaderVariants.Instance.GetVariant(shader, water.Materials.UsedKeywords);
			var boundingVolumes = water.Volume.GetVolumesDirect();
			RenderVolumes(commandBuffer, material, boundingVolumes, twoPass);

			var subtractiveVolumes = water.Volume.GetSubtractiveVolumesDirect();
			RenderVolumes(commandBuffer, material, subtractiveVolumes, twoPass);
		}

		public void RenderMasks(CommandBuffer commandBuffer)
		{
			for (int i = masks.Count - 1; i >= 0; --i)
				commandBuffer.DrawMesh(masks[i].GetComponent<MeshFilter>().sharedMesh, masks[i].transform.localToWorldMatrix, masks[i].sharedMaterial, 0, 0);
			//commandBuffer.DrawRenderer(masks[i], masks[i].sharedMaterial, 0, 0);
		}

		public void PostRender(Camera camera)
		{
			if(water != null)
				water.OnWaterPostRender(camera);

			ReleaseTemporaryBuffers();
		}

		public void OnSharedSubtractiveMaskRender(ref bool hasSubtractiveVolumes, ref bool hasAdditiveVolumes, ref bool hasFlatMasks)
		{
			if(!water.enabled) return;

			var boundingVolumes = water.Volume.GetVolumesDirect();
			int numBoundingVolumes = boundingVolumes.Count;

			for(int i = 0; i < numBoundingVolumes; ++i)
				boundingVolumes[i].DisableRenderers();

			var subtractiveVolumes = water.Volume.GetSubtractiveVolumesDirect();
			int numSubtractiveVolumes = subtractiveVolumes.Count;

			if(useSharedMask)
			{
				for(int i = 0; i < numSubtractiveVolumes; ++i)
					subtractiveVolumes[i].EnableRenderers(false);
				
				hasSubtractiveVolumes = hasSubtractiveVolumes || water.Volume.GetSubtractiveVolumesDirect().Count != 0;
				hasAdditiveVolumes = hasAdditiveVolumes || numBoundingVolumes != 0;
				hasFlatMasks = hasFlatMasks || masks.Count != 0;
			}
			else
			{
				for(int i = 0; i < numSubtractiveVolumes; ++i)
					subtractiveVolumes[i].DisableRenderers();
			}
		}

		public void OnSharedMaskAdditiveRender()
		{
			if(!water.enabled) return;

			if(useSharedMask)
			{
				var boundingVolumes = water.Volume.GetVolumesDirect();
				int numBoundingVolumes = boundingVolumes.Count;

				for(int i = 0; i < numBoundingVolumes; ++i)
					boundingVolumes[i].EnableRenderers(false);

				var subtractiveVolumes = water.Volume.GetSubtractiveVolumesDirect();
				int numSubtractiveVolumes = subtractiveVolumes.Count;

				for(int i = 0; i < numSubtractiveVolumes; ++i)
					subtractiveVolumes[i].DisableRenderers();
			}
		}

		public void OnSharedMaskPostRender()
		{
			if(!water.enabled) return;

			var boundingVolumes = water.Volume.GetVolumesDirect();
			int numBoundingVolumes = boundingVolumes.Count;

			for(int i = 0; i < numBoundingVolumes; ++i)
				boundingVolumes[i].EnableRenderers(true);

			var subtractiveVolumes = water.Volume.GetSubtractiveVolumesDirect();
			int numSubtractiveVolumes = subtractiveVolumes.Count;

			for(int i = 0; i < numSubtractiveVolumes; ++i)
				subtractiveVolumes[i].EnableRenderers(true);
		}

		private static void RenderVolumes<T>(CommandBuffer commandBuffer, Material material, List<T> boundingVolumes, bool twoPass) where T : WaterVolumeBase
		{
			for(int i = boundingVolumes.Count - 1; i >= 0; --i)
			{
				var volumeRenderers = boundingVolumes[i].VolumeRenderers;

				if(volumeRenderers == null || volumeRenderers.Length == 0 || !volumeRenderers[0].enabled)
					continue;

				if (!twoPass)
				{
					int passIndex = material.passCount == 1 ? 0 : 1;

					for (int ii = 0; ii < volumeRenderers.Length; ++ii)
						commandBuffer.DrawRenderer(volumeRenderers[ii], material, 0, passIndex);
				}
				else
				{
					for (int ii = 0; ii < volumeRenderers.Length; ++ii)
					{
						commandBuffer.DrawRenderer(volumeRenderers[ii], material, 0, 0);
						commandBuffer.DrawRenderer(volumeRenderers[ii], material, 0, 1);
					}
				}
			}
		}

		private void ReleaseTemporaryBuffers()
		{
			if(additiveMaskTexture != null)
			{
				RenderTexture.ReleaseTemporary(additiveMaskTexture);
				additiveMaskTexture = null;
			}

			if (subtractiveMaskTexture != null)
			{
				RenderTexture.ReleaseTemporary(subtractiveMaskTexture);
				subtractiveMaskTexture = null;
			}
		}

		private void RenderMasks(Camera camera, WaterCamera waterCamera, MaterialPropertyBlock propertyBlock)
		{
			var subtractiveVolumes = water.Volume.GetSubtractiveVolumesDirect();
			var additiveVolumes = water.Volume.GetVolumesDirect();

			if((object)waterCamera == null || !waterCamera.RenderVolumes || (subtractiveVolumes.Count == 0 && additiveVolumes.Count == 0 && masks.Count == 0))
			{
				ReleaseTemporaryBuffers();
				return;
			}

			int tempLayer = WaterProjectSettings.Instance.WaterTempLayer;
			int waterLayer = WaterProjectSettings.Instance.WaterCollidersLayer;

			var effectsCamera = waterCamera.EffectsCamera;

			if(effectsCamera == null)
			{
				ReleaseTemporaryBuffers();
				return;
			}

			bool t1 = false, t2 = false, t3 = false;
			OnSharedSubtractiveMaskRender(ref t1, ref t2, ref t3);

			effectsCamera.CopyFrom(camera);
			effectsCamera.enabled = false;
			effectsCamera.GetComponent<WaterCamera>().enabled = false;
			effectsCamera.renderingPath = RenderingPath.Forward;
			effectsCamera.depthTextureMode = DepthTextureMode.None;
			effectsCamera.cullingMask = 1 << tempLayer;
			
			if(subtractiveVolumes.Count != 0)
			{
				if(subtractiveMaskTexture == null)
					subtractiveMaskTexture = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight, 24, SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1);

				Graphics.SetRenderTarget(subtractiveMaskTexture);

				int numsubtractiveVolumes = subtractiveVolumes.Count;
				for(int i = 0; i < numsubtractiveVolumes; ++i)
					subtractiveVolumes[i].SetLayer(tempLayer);

				var volumeFrontTexture = RenderTexturesCache.GetTemporary(camera.pixelWidth, camera.pixelHeight, 24, SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf, true, false);

				// render front pass of volumetric masks
				effectsCamera.clearFlags = CameraClearFlags.SolidColor;
				effectsCamera.backgroundColor = new Color(0.0f, 0.0f, 0.5f, 0.0f);
				effectsCamera.targetTexture = volumeFrontTexture;
				effectsCamera.RenderWithShader(volumeFrontShader, "CustomType");

				GL.Clear(true, true, new Color(0.0f, 0.0f, 0.0f, 0.0f), 0.0f);

				// render back pass of volumetric masks
				Shader.SetGlobalTexture("_VolumesFrontDepth", volumeFrontTexture);
				effectsCamera.clearFlags = CameraClearFlags.Nothing;
				effectsCamera.targetTexture = subtractiveMaskTexture;
				effectsCamera.RenderWithShader(volumeBackShader, "CustomType");

				volumeFrontTexture.Dispose();

				for(int i = 0; i < numsubtractiveVolumes; ++i)
					subtractiveVolumes[i].SetLayer(waterLayer);

				propertyBlock.SetTexture(subtractiveMaskId, subtractiveMaskTexture);
			}

			if(additiveVolumes.Count != 0)
			{
				OnSharedMaskAdditiveRender();

				if(additiveMaskTexture == null)
					additiveMaskTexture = RenderTexture.GetTemporary(camera.pixelWidth, camera.pixelHeight, 16, SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear, 1);

				Graphics.SetRenderTarget(additiveMaskTexture);
				GL.Clear(true, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

				int numBoundingVolumes = additiveVolumes.Count;
				for (int i = 0; i < numBoundingVolumes; ++i)
				{
					additiveVolumes[i].SetLayer(tempLayer);
					additiveVolumes[i].EnableRenderers(false);
				}

				// render additive volumes
				effectsCamera.clearFlags = CameraClearFlags.Nothing;
				effectsCamera.targetTexture = additiveMaskTexture;
				effectsCamera.RenderWithShader(waterCamera.IsInsideAdditiveVolume ? volumeFrontShader : volumeFrontFastShader, "CustomType");

				effectsCamera.clearFlags = CameraClearFlags.Nothing;
				effectsCamera.targetTexture = additiveMaskTexture;
				effectsCamera.RenderWithShader(volumeBackShader, "CustomType");

				for(int i = 0; i < numBoundingVolumes; ++i)
					additiveVolumes[i].SetLayer(waterLayer);

				propertyBlock.SetTexture(additiveMaskId, additiveMaskTexture);
			}

			/*if(masks.Count != 0)
			{
				if(subtractiveVolumes.Count == 0 && additiveVolumes.Count == 0)
					GL.Clear(false, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

				int numMasks = masks.Count;
				for(int i = 0; i < numMasks; ++i)
					masks[i].enabled = true;

				// render simple "screen-space" masks
				effectsCamera.clearFlags = CameraClearFlags.Nothing;
				effectsCamera.targetTexture = additiveMaskTexture;
				effectsCamera.Render();

				for(int i = 0; i < numMasks; ++i)
					masks[i].enabled = false;
			}*/

			OnSharedMaskPostRender();

			effectsCamera.targetTexture = null;
		}
	}
}
