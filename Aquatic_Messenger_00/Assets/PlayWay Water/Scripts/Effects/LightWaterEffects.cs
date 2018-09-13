using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace PlayWay.Water
{
	/// <summary>
	/// Adds caustics effect to a directional light.
	/// </summary>
	[ExecuteInEditMode]
	[RequireComponent(typeof(Light))]
	public sealed class LightWaterEffects : MonoBehaviour
	{
		[HideInInspector] [SerializeField] private Shader worldPosShader;
		[HideInInspector] [SerializeField] private Shader causticsMapShader;
		[HideInInspector] [SerializeField] private Shader normalMapperShader;
		[HideInInspector] [SerializeField] private Shader causticUtilShader;
		
		[SerializeField] private bool castShadows = true;
		[SerializeField] private CausticsMode causticsMode = CausticsMode.ProjectedTexture;

		[Range(0.0f, 3.0f)]
		[SerializeField]
		private float intensity = 1.0f;
		
		// complex caustics fields
		[SerializeField]
		private LayerMask causticReceiversMask = int.MaxValue;
		
		[SerializeField]
		private Blur blur;

		[Tooltip("Causes minor allocation per frame (no way around it), but makes caustics rendering a lot faster. Disable it, if you don't use terrains.")]
		[SerializeField]
		private bool skipTerrainTrees = true;

		[FormerlySerializedAs("simpleCausticsTexture")]
		[SerializeField]
		private Texture2D projectedTexture;

		// projected texture fields
		[Tooltip("Optional.")]
		[SerializeField]
		private Transform scrollDirectionPointer;

		[SerializeField]
		private float uvScale = 1.0f;

		[Range(0.0f, 0.25f)]
		[SerializeField]
		private float scrollSpeed = 0.01f;
		
		[Range(0.0f, 8.0f)]
		[FormerlySerializedAs("distortions")]
		[SerializeField]
		private float distortions1 = 1.0f;

		[Range(0.0f, 8.0f)]
		[SerializeField]
		private float distortions2 = 1.0f;

		private Camera renderCamera;
		private WaterCamera waterCamera;
		private Material causticUtilMat;
        private Light localLight;
		private Vector2 offset;
		private Vector2 scroll;
		private bool hasDirectionPointer;
		private bool renderingPrepared;
		private bool[] terrainSettingTemp;
		private int id;
		private CommandBuffer copyShadowmap;

		private RenderTexture worldPosMap;
		private RenderTexture causticsMap;

		public static readonly List<LightWaterEffects> lights = new List<LightWaterEffects>();

		private static int shadowmapId;

		private void Awake()
		{
			shadowmapId = Shader.PropertyToID("_WaterShadowmap");
			terrainSettingTemp = new bool[32];

			localLight = GetComponent<Light>();
		}

		private void OnEnable()
		{
#if UNITY_EDITOR
			if (!Application.isPlaying)
				return;
#endif

			causticUtilMat = new Material(causticUtilShader) {hideFlags = HideFlags.DontSave};

			OnValidate();

			if(causticsMode != CausticsMode.None)
				CreateCausticsCamera();
			
			lights.Add(this);

			id = lights.Count - 1;

			Camera.onPreCull -= OnSomeCameraGlobalPreCull;
			Camera.onPreCull += OnSomeCameraGlobalPreCull;
		}

		private void OnDisable()
		{
#if UNITY_EDITOR
			if(!Application.isPlaying)
				return;
#endif

			Camera.onPreCull -= OnSomeCameraGlobalPreCull;

			lights.Remove(this);
			
			if(worldPosMap != null)
			{
				worldPosMap.Destroy();
				worldPosMap = null;
			}

			if(causticsMap != null)
			{
				causticsMap.Destroy();
				causticsMap = null;
			}

			if(renderCamera != null)
			{
				renderCamera.gameObject.Destroy();
				renderCamera = null;
            }
		}

		public void PrepareRenderingOnCamera(WaterCamera targetCamera)
		{
#if UNITY_EDITOR
			if(!Application.isPlaying)
				return;
#endif

			if (renderingPrepared)
				return;

			renderingPrepared = true;

			if(castShadows)
				PrepareShadows(targetCamera.CameraComponent);

			PrepareCaustics(targetCamera);
		}

		public void CleanRenderingOnCamera()
		{
			if(!renderingPrepared)
				return;
			
			renderingPrepared = false;

			if(copyShadowmap != null)
				localLight.RemoveCommandBuffer(LightEvent.AfterScreenspaceMask, copyShadowmap);
		}

		public Light UnityLight
		{
			get { return localLight; }
		}

		public bool CastShadows
		{
			get { return castShadows; }
			set { castShadows = value; }
		}
		
		private void OnValidate()
		{
			if(worldPosShader == null)
				worldPosShader = Shader.Find("PlayWay Water/Caustics/WorldPos");

			if(causticsMapShader == null)
				causticsMapShader = Shader.Find("PlayWay Water/Caustics/Map");

			if (normalMapperShader == null)
				normalMapperShader = Shader.Find("PlayWay Water/Caustics/NormalMapper");

			if(causticUtilShader == null)
				causticUtilShader = Shader.Find("PlayWay Water/Caustics/Utility");

			blur.Validate();
			hasDirectionPointer = scrollDirectionPointer != null;
		}

		public void AddWorldSpaceOffset(Vector3 offset)
		{
			this.offset.x += Vector3.Dot(offset, renderCamera.transform.right) * uvScale / (renderCamera.orthographicSize * 2.0f);
			this.offset.y += Vector3.Dot(offset, renderCamera.transform.up) * uvScale / (renderCamera.orthographicSize * 2.0f);
		}

		private void Update()
		{
			if (hasDirectionPointer)
			{
				Vector3 forward = scrollDirectionPointer.forward;
				float t = scrollSpeed*uvScale*Time.deltaTime;
				scroll.x += forward.x*t;
				scroll.y += forward.z*t;
			}
			else
			{
				float t = 0.7f*scrollSpeed*uvScale*Time.deltaTime;
				scroll.x += t;
				scroll.y += t;
			}
		}

		private void OnSomeCameraGlobalPreCull(Camera camera)
		{
			if(renderingPrepared)
				transform.position = new Vector3(id, 6.137f, 0.0f);
		}

		private void CreateCausticsCamera()
		{
			if (causticsMode == CausticsMode.ProjectedTexture)
			{
				causticsMap = new RenderTexture(256, 256, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
				{
					hideFlags = HideFlags.DontSave,
					wrapMode = TextureWrapMode.Repeat
				};
			}
			else
			{
				worldPosMap = new RenderTexture(256, 256, 32, RenderTextureFormat.Depth, RenderTextureReadWrite.Linear)
				{
					hideFlags = HideFlags.DontSave,
					wrapMode = TextureWrapMode.Clamp
				};

				causticsMap = new RenderTexture(512, 512, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
				{
					hideFlags = HideFlags.DontSave,
					wrapMode = TextureWrapMode.Clamp
				};
			}

			var renderCameraGo = new GameObject("Caustic Camera") {hideFlags = HideFlags.DontSave};
			renderCameraGo.transform.position = transform.position;
			renderCameraGo.transform.rotation = transform.rotation;

			renderCamera = renderCameraGo.AddComponent<Camera>();
			renderCamera.enabled = false;
            renderCamera.orthographic = true;
			renderCamera.orthographicSize = 85;
			renderCamera.farClipPlane = 5000;
			renderCamera.depthTextureMode = DepthTextureMode.None;
#if UNITY_5_6
			renderCamera.allowHDR = true;
#else
			renderCamera.hdr = true;
#endif
			renderCamera.useOcclusionCulling = false;
			renderCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			renderCamera.renderingPath = RenderingPath.VertexLit;

			waterCamera = renderCameraGo.AddComponent<WaterCamera>();
			waterCamera.RenderWaterDepth = false;
			waterCamera.RenderVolumes = false;
			waterCamera.Type = WaterCamera.CameraType.Effect;
			waterCamera.GeometryType = WaterGeometryType.UniformGrid;
		}

		private void PrepareShadows(Camera camera)
		{
			if (copyShadowmap == null)
				copyShadowmap = new CommandBuffer {name = "Water: Copy Shadowmap"};

			copyShadowmap.Clear();
			copyShadowmap.GetTemporaryRT(shadowmapId, camera.pixelWidth, camera.pixelHeight, 32, FilterMode.Point,
				RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			copyShadowmap.Blit(BuiltinRenderTextureType.CurrentActive, shadowmapId);

			localLight.AddCommandBuffer(LightEvent.AfterScreenspaceMask, copyShadowmap);
		}

		private void PrepareCaustics(WaterCamera waterCamera)
		{
			switch (causticsMode)
			{
				case CausticsMode.ProjectedTexture:
					UpdateCausticsCameraPosition();
					RenderProjectedTextureCaustics();
					break;

				case CausticsMode.Raymarching:
					UpdateCausticsCameraPosition();
					RenderRaymarchedCaustics(waterCamera);
					break;
			}
		}

		private void UpdateCausticsCameraPosition()
		{
			Vector2 center = waterCamera.LocalMapsRect.center;
			renderCamera.transform.position = new Vector3(0, 0, 0);//= new Vector3(center.x, 0.0f, center.y) - transform.forward * Mathf.Max(Mathf.Abs(waterCamera.transform.position.y * 2.2f), 300.0f);
			renderCamera.transform.rotation = transform.rotation;
		}

		private void RenderProjectedTextureCaustics()
		{
			/*this.waterCamera.enabled = true;
			renderCamera.clearFlags = CameraClearFlags.Color;
			renderCamera.cullingMask = (1 << WaterProjectSettings.Instance.WaterLayer);
			renderCamera.targetTexture = causticsMap;
			renderCamera.RenderWithShader(normalMapperShader, "CustomType");*/

			renderCamera.cullingMask = 1 << WaterProjectSettings.Instance.WaterLayer;
			waterCamera.RenderWaterWithShader("[PW Water] Caustics Normal Map", causticsMap, normalMapperShader, true, false, false);

			Vector3 pos = renderCamera.transform.position;
			float x = Vector3.Dot(pos, renderCamera.transform.right)*uvScale/(renderCamera.orthographicSize*2.0f);
			float y = Vector3.Dot(pos, renderCamera.transform.up)*uvScale/(renderCamera.orthographicSize*2.0f);

			Shader.SetGlobalTexture("_CausticsMap", projectedTexture);
			Shader.SetGlobalTexture("_CausticsDistortionMap", causticsMap);
			Shader.SetGlobalFloat("_CausticsMultiplier", intensity*5.0f);
			Shader.SetGlobalVector("_CausticsOffsetScale", new Vector4(offset.x + scroll.x + x, offset.y + scroll.y + y, uvScale, distortions1*0.02f));
			Shader.SetGlobalVector("_CausticsOffsetScale2", new Vector4(offset.x - scroll.x + x + 0.5f, offset.y - scroll.y + y, uvScale, distortions2*0.02f));
			Shader.SetGlobalMatrix("_CausticsMapProj", GL.GetGPUProjectionMatrix(renderCamera.projectionMatrix, true)*renderCamera.worldToCameraMatrix);
		}
		
		private void RenderRaymarchedCaustics(WaterCamera waterCamera)
		{
			causticUtilMat.SetMatrix("_InvProjMatrix", Matrix4x4.Inverse(renderCamera.projectionMatrix * renderCamera.worldToCameraMatrix));
			
			Graphics.SetRenderTarget(worldPosMap);
			GL.Clear(true, true, new Color(1.0f, 0.0f, 0.0f, 0.0f), 1.0f);

			Terrain[] terrains = null;

			if(skipTerrainTrees)
			{
				terrains = Terrain.activeTerrains;

				if(terrainSettingTemp.Length < terrains.Length)
					System.Array.Resize(ref terrainSettingTemp, terrains.Length * 2);

				for(int i = 0; i < terrains.Length; ++i)
				{
					terrainSettingTemp[i] = terrains[i].drawTreesAndFoliage;
					terrains[i].drawTreesAndFoliage = false;
				}
			}

			this.waterCamera.enabled = false;
			renderCamera.orthographicSize = waterCamera.LocalMapsRect.width * 0.6f;
			renderCamera.clearFlags = CameraClearFlags.Depth;
			renderCamera.cullingMask = causticReceiversMask;
			renderCamera.targetTexture = worldPosMap;
			renderCamera.RenderWithShader(worldPosShader, "RenderType");

			if(skipTerrainTrees)
			{
				// ReSharper disable once PossibleNullReferenceException
				for(int i = 0; i < terrains.Length; ++i)
					terrains[i].drawTreesAndFoliage = terrainSettingTemp[i];
			}

			Shader.SetGlobalTexture("_WorldPosMap", worldPosMap);
			Shader.SetGlobalVector("_CausticLightDir", transform.forward);
			Shader.SetGlobalFloat("_CausticLightIntensity", localLight.intensity * intensity * 1.5f);
			
			this.waterCamera.enabled = true;
			renderCamera.clearFlags = CameraClearFlags.Color;
			renderCamera.cullingMask = (1 << WaterProjectSettings.Instance.WaterLayer);
			renderCamera.targetTexture = causticsMap;
			renderCamera.RenderWithShader(causticsMapShader, "CustomType");

			//blur.TotalSize = baseBlurSize / waterCamera.LocalMapsRect.width;
			blur.Apply(causticsMap);
			
            Graphics.Blit(null, causticsMap, causticUtilMat, 1);
			
			Shader.SetGlobalTexture("_CausticsMap", causticsMap);
			Shader.SetGlobalFloat("_CausticsMultiplier", 1.0f);
			Shader.SetGlobalMatrix("_CausticsMapProj", GL.GetGPUProjectionMatrix(renderCamera.projectionMatrix, true) * renderCamera.worldToCameraMatrix);
		}

		public enum CausticsMode
		{
			None,
			ProjectedTexture,
			Raymarching
		}
	}
}
