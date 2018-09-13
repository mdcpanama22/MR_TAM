using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	public class DynamicWater
	{
		[System.Serializable]
		public class Data
		{
			public int Antialiasing = 1;
			public LayerMask InteractionMask = -1;

			public bool RenderCustomEffects;
			public LayerMask CustomEffectsLayerMask;
		}

		private IOverlaysRenderer[] overlayRenderers;
		private Material mapLocalDisplacementsMaterial;

		private readonly Water water;
		private readonly Data data;
		private readonly Shader mapLocalDisplacementsShader;
		private readonly Shader waterInteractionMaskShader;

		private CommandBuffer renderCommandBuffer;
		private readonly Dictionary<Camera, DynamicWaterCameraData> buffers = new Dictionary<Camera, DynamicWaterCameraData>();
		private readonly List<Camera> lostCameras = new List<Camera>();
		private static RenderTargetIdentifier[] customEffectsBuffers = new RenderTargetIdentifier[3];
		private static readonly List<IWaterInteraction> interactionRenderers = new List<IWaterInteraction>();
		private static int localDisplacementMapId, localNormalMapId, totalDisplacementMapId, displacementsMaskId;

		public DynamicWater(Water water, Data data)
		{
			this.water = water;
			this.data = data;

			if (renderCommandBuffer == null)
			{
				renderCommandBuffer = new CommandBuffer();
				renderCommandBuffer.name = "[PlayWay Water] Render Water Surface Overlays";
			}

			Validate();
			
			mapLocalDisplacementsShader = Shader.Find("PlayWay Water/Utility/Map Local Displacements");
			waterInteractionMaskShader = Shader.Find("PlayWay Water/Utility/ShorelineMaskRender");
		}

		internal void Enable()
		{
			WaterGlobals.Instance.AddDynamicWater(this);
			CacheShaderProperties();
			ValidateWaterComponents();

			if(mapLocalDisplacementsMaterial == null)
				mapLocalDisplacementsMaterial = new Material(mapLocalDisplacementsShader) { hideFlags = HideFlags.DontSave };
		}

		internal void Disable()
		{
			WaterGlobals.Instance.RemoveDynamicWater(this);

			var enumerator = buffers.GetEnumerator();
			while(enumerator.MoveNext())
				enumerator.Current.Value.Dispose();
			buffers.Clear();
		}

		public Water Water
		{
			get { return water; }
		}

		internal void Destroy()
		{
			
		}

		internal void Validate()
		{
			
		}

		internal void Update()
		{
			int frameIndex = Time.frameCount - 3;

			var enumerator = buffers.GetEnumerator();
			while(enumerator.MoveNext())
			{
				if(enumerator.Current.Value.lastFrameUsed < frameIndex)
				{
					enumerator.Current.Value.Dispose();
					lostCameras.Add(enumerator.Current.Key);
				}
			}

			for(int i = 0; i < lostCameras.Count; ++i)
				buffers.Remove(lostCameras[i]);

			lostCameras.Clear();
		}

		public void ValidateWaterComponents()
		{
			overlayRenderers = water.GetComponentsInChildren<IOverlaysRenderer>();
			var priorities = new int[overlayRenderers.Length];

			for(int i = 0; i < priorities.Length; ++i)
			{
				var type = overlayRenderers[i].GetType();
				var attributes = type.GetCustomAttributes(typeof(OverlayRendererOrderAttribute), true);

				if(attributes.Length != 0)
					priorities[i] = ((OverlayRendererOrderAttribute)attributes[0]).Priority;
			}

			System.Array.Sort(priorities, overlayRenderers);
		}
		
		public void OnWaterRender(Camera camera)
		{
			var waterCamera = camera.GetComponent<WaterCamera>();

			if(waterCamera == null || waterCamera.Type != WaterCamera.CameraType.Normal || !Application.isPlaying || WaterCamera.IsSceneViewCamera(camera))
				return;

			var overlays = GetCameraOverlaysData(camera);
			overlays.ClearOverlays();

			RenderInteractions(overlays);

			for(int i = 0; i < overlayRenderers.Length; ++i)
				overlayRenderers[i].RenderOverlays(overlays);

			if(water.Foam != null)
				water.Foam.RenderOverlays(overlays);

			if(data.RenderCustomEffects)
				RenderCustomEffects(overlays);

			for(int i = interactionRenderers.Count - 1; i >= 0; --i)
				interactionRenderers[i].OnInteractionPostRender(overlays);

			for(int i = 0; i < overlayRenderers.Length; ++i)
				overlayRenderers[i].RenderFoam(overlays);

			if(data.RenderCustomEffects)
				RenderCustomEffectsFoam(overlays);

			var block = water.Renderer.PropertyBlock;
			block.SetTexture(localDisplacementMapId, overlays.DynamicDisplacementMap);
			block.SetTexture(localNormalMapId, overlays.NormalMap);
			block.SetTexture(totalDisplacementMapId, overlays.GetTotalDisplacementMap());
			block.SetTexture(displacementsMaskId, overlays.DisplacementsMask);

			var debugMap = overlays.GetDebugMap();

			if(debugMap != null)
				block.SetTexture("_LocalDebugMap", debugMap);

			if(waterCamera.MainWater == water && Camera.main == camera)
				Shader.SetGlobalTexture(totalDisplacementMapId, overlays.GetTotalDisplacementMap());
		}

		public void RenderTotalDisplacementMap(WaterCamera camera, RenderTexture renderTexture)
		{
			Rect rect = camera.LocalMapsRect;
			
			var effectsCamera = camera.EffectsCamera;
			effectsCamera.enabled = false;
			effectsCamera.stereoTargetEye = StereoTargetEyeMask.None;
			effectsCamera.depthTextureMode = DepthTextureMode.None;
			effectsCamera.renderingPath = RenderingPath.VertexLit;
			effectsCamera.orthographic = true;
			effectsCamera.orthographicSize = rect.width * 0.5f;
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

			var effectsWaterCamera = effectsCamera.GetComponent<WaterCamera>();
			effectsWaterCamera.enabled = true;
			effectsWaterCamera.GeometryType = WaterGeometryType.UniformGrid;
			effectsWaterCamera.ForcedVertexCount = (renderTexture.width * renderTexture.height) >> 2;
			effectsWaterCamera.RenderWaterWithShader("[PW Water] Render Total Displacement Map", renderTexture, mapLocalDisplacementsShader, water);
		}
		
		public DynamicWaterCameraData GetCameraOverlaysData(Camera camera, bool createIfNotExists = true)
		{
			DynamicWaterCameraData overlaysData;

			if(!buffers.TryGetValue(camera, out overlaysData) && createIfNotExists)
			{
				buffers[camera] = overlaysData = new DynamicWaterCameraData(this, WaterCamera.GetWaterCamera(camera), data.Antialiasing);

				RenderInteractions(overlaysData);
				overlaysData.SwapFoamMaps();

				for(int i = 0; i < overlayRenderers.Length; ++i)
					overlayRenderers[i].RenderOverlays(overlaysData);

				overlaysData.Initialization = false;
			}

			if(overlaysData != null)
				overlaysData.lastFrameUsed = Time.frameCount;

			return overlaysData;
		}

		private void RenderInteractions(DynamicWaterCameraData overlays)
		{
			Rect rect = overlays.Camera.LocalMapsRect;

			if(rect.width == 0.0f)
				return;

			int numInteractionRenderers = interactionRenderers.Count;

			if(numInteractionRenderers != 0)
			{
				var effectsCamera = overlays.Camera.PlaneProjectorCamera;
				effectsCamera.enabled = false;
				effectsCamera.depthTextureMode = DepthTextureMode.None;
				effectsCamera.renderingPath = RenderingPath.VertexLit;
				effectsCamera.cullingMask = 1 << WaterProjectSettings.Instance.WaterTempLayer;
				effectsCamera.clearFlags = CameraClearFlags.Nothing;
				effectsCamera.targetTexture = overlays.DisplacementsMask;

				var windWaves = water.WindWaves;
				Shader.SetGlobalVector("_TileSizesInv", windWaves.TileSizesInv);

				//for(int i = 0; i < numInteractionRenderers; ++i)
				//	interactionRenderers[i].OnInteractionPreRender(effectsCamera, water.transform.position.y, data.InteractionMask);
				//
				//effectsCamera.RenderWithShader(waterInteractionMaskShader, "CustomType");

				GL.PushMatrix();
				GL.modelview = effectsCamera.worldToCameraMatrix;
				GL.LoadProjectionMatrix(effectsCamera.projectionMatrix);

				Graphics.SetRenderTarget(overlays.DisplacementsMask);

				for (int i = 0; i < numInteractionRenderers; ++i)
				{
					interactionRenderers[i].OnInteractionPreRender(effectsCamera, water.transform.position.y, data.InteractionMask);
					interactionRenderers[i].RenderInteractionDirect();
				}

				GL.PopMatrix();
			}
		}

		private void RenderCustomEffects(DynamicWaterCameraData overlays)
		{
			var effectsCamera = overlays.Camera.PlaneProjectorCamera;

			customEffectsBuffers[0] = overlays.DynamicDisplacementMap;
			customEffectsBuffers[1] = overlays.NormalMap;
			customEffectsBuffers[2] = overlays.DisplacementsMask;

			GL.PushMatrix();
			GL.modelview = effectsCamera.worldToCameraMatrix;
			GL.LoadProjectionMatrix(effectsCamera.projectionMatrix);
			
			renderCommandBuffer.Clear();
			renderCommandBuffer.SetRenderTarget(customEffectsBuffers, overlays.DynamicDisplacementMap);

			var renderers = WaterSurfaceOverlayRenderer.list;

			for (int i = renderers.Count - 1; i >= 0; --i)
			{
				if(((1 << renderers[i].gameObject.layer) & data.CustomEffectsLayerMask) != 0)
					renderCommandBuffer.DrawRenderer(renderers[i].Renderer, renderers[i].DisplacementNormalMaterial);
			}

			Graphics.ExecuteCommandBuffer(renderCommandBuffer);
		}

		private void RenderCustomEffectsFoam(DynamicWaterCameraData overlays)
		{
			Graphics.SetRenderTarget(overlays.FoamMap.colorBuffer, overlays.FoamMap.depthBuffer);
			renderCommandBuffer.Clear();

			var renderers = WaterSurfaceOverlayRenderer.list;

			for (int i = renderers.Count - 1; i >= 0; --i)
			{
				if(((1 << renderers[i].gameObject.layer) & data.CustomEffectsLayerMask) != 0)
					renderCommandBuffer.DrawRenderer(renderers[i].Renderer, renderers[i].FoamMaterial);
			}

			Graphics.ExecuteCommandBuffer(renderCommandBuffer);
			GL.PopMatrix();
		}

		public static void RegisterInteraction(IWaterInteraction renderer)
		{
			interactionRenderers.Add(renderer);
		}

		public static void UnregisterInteraction(IWaterInteraction renderer)
		{
			interactionRenderers.Remove(renderer);
		}

		private static void CacheShaderProperties()
		{
			if(localDisplacementMapId == 0)
			{
				localDisplacementMapId = Shader.PropertyToID("_LocalDisplacementMap");
				localNormalMapId = Shader.PropertyToID("_LocalNormalMap");
				totalDisplacementMapId = Shader.PropertyToID("_TotalDisplacementMap");
				displacementsMaskId = Shader.PropertyToID("_DisplacementsMask");
			}
		}
	}
}
