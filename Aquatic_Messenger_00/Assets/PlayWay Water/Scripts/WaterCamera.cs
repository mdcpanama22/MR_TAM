using System.Collections.Generic;
using PlayWay.Water.Internal;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	/// <summary>
	///     Each camera supposed to see water needs this component attached. Renders all camera-specific maps for the water:
	///     <list type="bullet">
	///         <item>Depth Maps</item>
	///         <item>Displaced water info map</item>
	///         <item>Volume maps</item>
	///     </list>
	/// </summary>
#if UNITY_5_4_OR_NEWER
	[ImageEffectAllowedInSceneView]
#endif
	[AddComponentMenu("Water/Water Camera", -1)]
	[ExecuteInEditMode]
	public class WaterCamera : MonoBehaviour
	{
		public enum CameraType
		{
			/// <summary>
			/// It's a camera that has attached WaterCamera component.
			/// </summary>
			Normal,

			/// <summary>
			/// It's a camera used for depth rendering etc.
			/// </summary>
			Effect,

			/// <summary>
			/// It's a camera used for rendering water before displaying it in image effect render modes.
			/// </summary>
			RenderHelper
		}

		[HideInInspector]
		[SerializeField]
		private Shader depthBlitCopyShader;

		[HideInInspector]
		[SerializeField]
		private Shader waterDepthShader;

		[HideInInspector]
		[SerializeField]
		private Shader volumeFrontShader;

		[HideInInspector]
		[SerializeField]
		private Shader volumeBackShader;

		[HideInInspector]
		[SerializeField]
		private Shader volumeFrontFastShader;

		[HideInInspector]
		[SerializeField]
		private Shader shadowEnforcerShader;

		[HideInInspector]
		[SerializeField]
		private Shader gbuffer0MixShader;

		[HideInInspector]
		[SerializeField]
		private Shader gbuffer123MixShader;

		[HideInInspector]
		[SerializeField]
		private Shader finalColorMixShader;

		[HideInInspector]
		[SerializeField]
		private Shader deferredReflections;

		[HideInInspector]
		[SerializeField]
		private Shader deferredShading;
		
		[HideInInspector]
		[SerializeField]
		private Shader mergeDisplacementsShader;

		[SerializeField]
		private WaterRenderMode renderMode;

		[SerializeField]
		private WaterGeometryType geometryType = WaterGeometryType.Auto;

		[SerializeField]
		private bool renderWaterDepth = true;

		[Tooltip("Water has a pretty smooth shape so it's often safe to render it's depth in a lower resolution than the rest of the scene. Although the default value is 1.0, you may probably safely use 0.5 and gain some minor performance boost. If you will encounter any artifacts in masking or image effects, set it back to 1.0.")]
		[Range(0.2f, 1.0f)]
		[SerializeField]
		private float baseEffectsQuality = 1.0f;

		[SerializeField]
		private float superSampling = 1.0f;

		[SerializeField]
		private bool renderVolumes = true;

		[SerializeField]
		private bool renderFlatMasks = true;
		
		[SerializeField]
		private int forcedVertexCount = 0;

		[SerializeField]
		private WaterCameraEvent submersionStateChanged;

		[Tooltip("Optional. Deferred rendering mode will try to match profile parameters of this water object as well as possible. It affects only some minor parameters and you may generally ignore this setting. May be removed in the future.")]
		[SerializeField]
		private Water mainWater;

		[SerializeField]
		private bool singlePassStereoRendering;

		[SerializeField]
		private LightWaterEffects effectsLight;
		
		private RenderTexture gbuffer0Tex, depthTex2;
		private CommandBuffer depthRenderCommands;
		private CommandBuffer volumeRenderCommands;
		private CommandBuffer cleanUpCommands;
		private WaterCamera baseCamera;
		private Camera effectCamera;
		private Camera mainCamera;
		private Camera planeProjectorCamera;
		protected Camera cameraComponent;
		private Material depthBlitCopyMaterial;
		private RenderTextureFormat waterDepthTextureFormat;
		private RenderTextureFormat blendedDepthTexturesFormat;
		private CameraType waterCameraType;
		private bool effectsEnabled;
		private IWaterImageEffect[] imageEffects;
		private Rect localMapsRect;
		private Rect localMapsRectPrevious;
		private Rect shadowedWaterRect;
		private int pixelWidth, pixelHeight;
		private Mesh shadowsEnforcerMesh;
		private Material shadowsEnforcerMaterial;
		private Water containingWater;
		private WaterSample waterSample;
		private float waterLevel;
		private SubmersionState submersionState;
		private bool isInsideSubtractiveVolume;
		private bool isInsideAdditiveVolume;
		private Matrix4x4 lastPlaneProjectorMatrix;
		private WaterCameraIME waterCameraIME;
		private List<Water> customWaterRenderList;

		private static int waterDepthTextureId = -1;
		private static int underwaterMaskId, additiveMaskId, subtractiveMaskId, displacementsMaskId, unityMatrixVPInverseId, depthClipMultiplierId, localMapsCoordsId, localMapsCoordsPreviousId;

		public static event System.Action<WaterCamera> OnGlobalPreCull;
		public static event System.Action<WaterCamera> OnGlobalPostRender;

		public event System.Action<WaterCamera> RenderTargetResized;
		public event System.Action<WaterCamera> Destroyed;
		public event System.Action<WaterCamera> Disabled;

		private static CommandBuffer utilityCommandBuffer;
		private static readonly Dictionary<Camera, WaterCamera> waterCamerasCache = new Dictionary<Camera, WaterCamera>();
		private static readonly List<WaterCamera> enabledWaterCameras = new List<WaterCamera>();
		private static readonly RenderTargetIdentifier[] deferredTargets = new RenderTargetIdentifier[] { BuiltinRenderTextureType.GBuffer1, BuiltinRenderTextureType.GBuffer2, BuiltinRenderTextureType.Reflections };
        
		protected void Awake()
		{
			if(waterDepthTextureId == -1)
				InitializeStaticFields();

			OnValidate();

			if(SystemInfo.graphicsShaderLevel >= 50 && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth) && DepthBlitCopyMaterial.passCount > 3)
			{
				waterDepthTextureFormat = RenderTextureFormat.Depth;            // only >= 4.0 shader targets can copy depth textures
				blendedDepthTexturesFormat = RenderTextureFormat.Depth;
			}
			else
			{
				if(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat) && baseEffectsQuality > 0.2f)
					blendedDepthTexturesFormat = RenderTextureFormat.RFloat;
				else if(SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf))
					blendedDepthTexturesFormat = RenderTextureFormat.RHalf;
				else
					blendedDepthTexturesFormat = RenderTextureFormat.R8;

				waterDepthTextureFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth) ? RenderTextureFormat.Depth : blendedDepthTexturesFormat;
			}

			gbuffer0MixMaterial = new Material(gbuffer0MixShader) {hideFlags = HideFlags.DontSave};
			gbuffer123MixMaterial = new Material(gbuffer123MixShader) {hideFlags = HideFlags.DontSave};
			finalColorMixMaterial = new Material(finalColorMixShader) {hideFlags = HideFlags.DontSave};
		}

		protected void OnEnable()
		{
			cameraComponent = GetComponent<Camera>();
			waterCamerasCache[cameraComponent] = this;

			if(waterCameraType == CameraType.Normal)
			{
				enabledWaterCameras.Add(this);
				imageEffects = GetComponents<IWaterImageEffect>();

				foreach(var imageEffect in imageEffects)
					imageEffect.OnWaterCameraEnabled();
			}

			RemoveUtilityCommands();
			AddUtilityCommands();
		}

		protected void OnDisable()
		{
			if(waterCameraType == CameraType.Normal)
				enabledWaterCameras.Remove(this);

			ReleaseImageEffectTemporaryTextures();
			ReleaseTemporaryTextures();

			RemoveUtilityCommands();
			DisableEffects();

			if(effectCamera != null)
			{
				effectCamera.gameObject.Destroy();
				effectCamera = null;
			}

			if(planeProjectorCamera != null)
			{
				planeProjectorCamera.gameObject.Destroy();
				planeProjectorCamera = null;
			}

			if(depthBlitCopyMaterial != null)
			{
				depthBlitCopyMaterial.Destroy();
				depthBlitCopyMaterial = null;
			}

			if(waterSample != null)
			{
				waterSample.Stop();
				waterSample = null;
			}

			containingWater = null;

			if(Disabled != null)
				Disabled(this);
        }

		private void OnDestroy()
		{
			waterCamerasCache.Remove(GetComponent<Camera>());

			if(Destroyed != null)
			{
				Destroyed(this);
				Destroyed = null;
            }
        }

		public bool RenderWaterDepth
		{
			get { return renderWaterDepth; }
			set { renderWaterDepth = value; }
		}

		public bool RenderVolumes
		{
			get { return renderVolumes; }
			set { renderVolumes = value; }
		}

		public float BaseEffectsQuality
		{
			get { return baseEffectsQuality; }
		}

		public CameraType Type
		{
			get { return waterCameraType; }
			set { waterCameraType = value; }
		}

		public WaterGeometryType GeometryType
		{
			get { return geometryType; }
			set { geometryType = value; }
		}

		public Rect LocalMapsRect
		{
			get { return localMapsRect; }
		}

		public WaterRenderMode RenderMode
		{
			get { return renderMode; }
			set
			{
				renderMode = value;
				OnDisable();
				OnEnable();
			}
		}

		public bool SinglePassStereoRendering
		{
			get { return singlePassStereoRendering; }
		}

		public Rect LocalMapsRectPrevious
		{
			get { return localMapsRectPrevious; }
		}

		public Vector4 LocalMapsShaderCoords
		{
			get
			{
				float invWidth = 1.0f/localMapsRect.width;
				return new Vector4(-localMapsRect.xMin * invWidth, -localMapsRect.yMin * invWidth, invWidth, localMapsRect.width);
			}
		}

		public int ForcedVertexCount
		{
			get { return forcedVertexCount; }
			set { forcedVertexCount = value; }
		}

		public Water ContainingWater
		{
			get { return baseCamera == null ? (submersionState != SubmersionState.None ? containingWater : null) : baseCamera.ContainingWater; }
		}

		public float WaterLevel
		{
			get { return waterLevel; }
		}
		
		public SubmersionState SubmersionState
		{
			get { return submersionState; }
		}

		public Camera MainCamera
		{
			get { return mainCamera; }
		}

		public Camera CameraComponent
		{
			get { return cameraComponent; }
		}

		protected Material DepthBlitCopyMaterial
		{
			get {
				return depthBlitCopyMaterial != null ? depthBlitCopyMaterial :
				       (depthBlitCopyMaterial = new Material(depthBlitCopyShader) {hideFlags = HideFlags.DontSave});
			}
		}

		private static CommandBuffer UtilityCommandBuffer
		{
			get
			{
				if (utilityCommandBuffer == null)
				{
					utilityCommandBuffer = new CommandBuffer {name = "[PW Water] WaterCamera Utility" };
				}

				return utilityCommandBuffer;
			}
		}

		public Water MainWater
		{
			get
			{
				if(mainWater != null)
				{
					return mainWater;
				}
				else
				{
					var boundlessWaters = WaterGlobals.Instance.BoundlessWaters;

					if(boundlessWaters.Count != 0)
						return boundlessWaters[0];

					var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

					if(waters.Count != 0)
						return waters[0];

					return null;
				}
			}
		}

		public static List<WaterCamera> EnabledWaterCameras
		{
			get { return enabledWaterCameras; }
		}

		/// <summary>
		/// Ready to render alternative camera for effects.
		/// </summary>
		public Camera EffectsCamera
		{
			get
			{
				if(waterCameraType == CameraType.Normal && effectCamera == null)
					effectCamera = CreateEffectsCamera(CameraType.Effect);

				return effectCamera;
			}
		}

		public Camera PlaneProjectorCamera
		{
			get
			{
				if(waterCameraType == CameraType.Normal && planeProjectorCamera == null)
					planeProjectorCamera = CreateEffectsCamera(CameraType.Effect);

				return planeProjectorCamera;
			}
		}
		
		public WaterCameraEvent SubmersionStateChanged
		{
			get { return submersionStateChanged ?? (submersionStateChanged = new WaterCameraEvent()); }
		}

		internal Camera WaterRenderCamera
		{
			get { return waterRenderCamera ?? (waterRenderCamera = CreateEffectsCamera(CameraType.RenderHelper)); }
		}

		public bool IsInsideAdditiveVolume
		{
			get { return isInsideAdditiveVolume; }
		}

		public LightWaterEffects EffectsLight
		{
			get { return effectsLight; }
			set { effectsLight = value; }
		}

		private void OnValidate()
		{
			if(depthBlitCopyShader == null)
				depthBlitCopyShader = Shader.Find("PlayWay Water/Depth/Depth Copy");

			if(waterDepthShader == null)
				waterDepthShader = Shader.Find("PlayWay Water/Depth/Water Depth");

			if(volumeFrontShader == null)
				volumeFrontShader = Shader.Find("PlayWay Water/Volumes/Front");

			if(volumeBackShader == null)
				volumeBackShader = Shader.Find("PlayWay Water/Volumes/Back");

			if(volumeFrontFastShader == null)
				volumeFrontFastShader = Shader.Find("PlayWay Water/Volumes/Front Simple");

			if(shadowEnforcerShader == null)
				shadowEnforcerShader = Shader.Find("PlayWay Water/Utility/ShadowEnforcer");

			if(gbuffer0MixShader == null)
				gbuffer0MixShader = Shader.Find("PlayWay Water/Deferred/GBuffer0Mix");

			if(gbuffer123MixShader == null)
				gbuffer123MixShader = Shader.Find("PlayWay Water/Deferred/GBuffer123Mix");

			if(finalColorMixShader == null)
				finalColorMixShader = Shader.Find("PlayWay Water/Deferred/FinalColorMix");

			if(deferredReflections == null)
				deferredReflections = Shader.Find("Hidden/PlayWay Water-Internal-DeferredReflections");

			if(deferredShading == null)
				deferredShading = Shader.Find("Hidden/PlayWay Water-Internal-DeferredShading");

			if (mergeDisplacementsShader == null)
				mergeDisplacementsShader = Shader.Find("PlayWay Water/Utility/MergeDisplacements");

			if(waterCameraIME == null)
				waterCameraIME = GetComponent<WaterCameraIME>() ?? gameObject.AddComponent<WaterCameraIME>();

			if(renderMode == WaterRenderMode.ImageEffectDeferred)
			{
				renderWaterDepth = true;
				baseEffectsQuality = 1.0f;
			}

			cameraComponent = GetComponent<Camera>();

#if UNITY_EDITOR
			ReorderWaterCameraIME();
#endif

			RemoveUtilityCommands();

			if(enabled)
				AddUtilityCommands();

			waterCameraIME.enabled = enabled && (renderMode == WaterRenderMode.ImageEffectDeferred || renderMode == WaterRenderMode.ImageEffectForward);
		}

#if UNITY_EDITOR
		private void ReorderWaterCameraIME()
		{
			var components = GetComponents<Component>();

			int index = components.Length - 1;

			while(!(components[index] is WaterCameraIME))
			{
				if(components[index] is WaterCamera)
				{
					while(!(components[index--] is WaterCameraIME))
						UnityEditorInternal.ComponentUtility.MoveComponentDown(waterCameraIME);

					return;
				}

				--index;
			}

			--index;

			while(!(components[index--] is WaterCamera))
				UnityEditorInternal.ComponentUtility.MoveComponentUp(waterCameraIME);
		}
#endif

		protected void OnPreCull()
		{
			if(!enabled)
				return;

			bool isSceneViewCamera = IsSceneViewCamera(cameraComponent);

#if UNITY_EDITOR
			if (isSceneViewCamera)
				effectsLight = null;
#endif

			if(OnGlobalPreCull != null && !isSceneViewCamera)
				OnGlobalPreCull(this);

			if(waterCameraType == CameraType.RenderHelper)
			{
				mainCamera.GetComponent<WaterCamera>().RenderWaterDirect();
				return;
			}

			bool hasEffectsLight = effectsLight != null;

			if(waterCameraType == CameraType.Normal)
			{
				SetPlaneProjectorMatrix();

				if(!isSceneViewCamera)
					ToggleEffects();

				PrepareToRender();
				SetFallbackTextures();
			}

			if(effectsEnabled)
				SetLocalMapCoordinates();

			RenderWaterEffects();

			if (hasEffectsLight)
				effectsLight.PrepareRenderingOnCamera(this);

			if(renderMode == WaterRenderMode.DefaultQueue)
				RenderWaterDirect();

#if UNITY_EDITOR
			if(isSceneViewCamera)
			{
				if(waterCameraType == CameraType.Normal)
				{
					SetBlankWaterMasks();

					if(effectCamera != null)
					{
						DestroyImmediate(effectCamera.gameObject);
						effectCamera = null;
					}
				}

				return;
			}
#endif

			if(!effectsEnabled) return;

			if(renderVolumes)
				RenderWaterMasks(pixelWidth, pixelHeight);
			else
				SetBlankWaterMasks();

			if(renderWaterDepth && (renderMode != WaterRenderMode.ImageEffectDeferred || (hasEffectsLight && effectsLight.CastShadows)))
				RenderWaterDepthBuffer(pixelWidth, pixelHeight);

			if(imageEffects != null && Application.isPlaying)
			{
				for(int i=0; i<imageEffects.Length; ++i)
					imageEffects[i].OnWaterCameraPreCull();
			}

			if(shadowedWaterRect.xMin < shadowedWaterRect.xMax)
				RenderShadowEnforcers();

			if(renderMode != WaterRenderMode.DefaultQueue)
			{
				var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

				for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
					waters[waterIndex].Volume.DisableRenderers();

				WaterMaterials.ValidateGlobalWaterDataLookupTex();
			}
		}
		
		protected void OnPostRender()
		{
			ReleaseTemporaryTextures();

			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

			for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
				waters[waterIndex].Renderer.PostRender(cameraComponent);

			if((object)effectsLight != null)
				effectsLight.CleanRenderingOnCamera();

			if(OnGlobalPostRender != null)
				OnGlobalPostRender(this);
        }

		internal void ReportShadowedWaterMinMaxRect(Vector2 min, Vector2 max)
		{
			if(shadowedWaterRect.xMin > min.x)
				shadowedWaterRect.xMin = min.x;

			if(shadowedWaterRect.yMin > min.y)
				shadowedWaterRect.yMin = min.y;

			if(shadowedWaterRect.xMax < max.x)
				shadowedWaterRect.xMax = max.x;

			if(shadowedWaterRect.yMax < max.y)
				shadowedWaterRect.yMax = max.y;
		}

		public void ReleaseTemporaryTextures()
		{
			if(gbuffer0Tex != null)
			{
				RenderTexture.ReleaseTemporary(gbuffer0Tex);
				gbuffer0Tex = null;
            }

			if (depthTex2 != null)
			{
				RenderTexture.ReleaseTemporary(depthTex2);
				depthTex2 = null;
			}
		}

		public void CopyFrom(WaterCamera waterCamera)
		{
			localMapsRect = waterCamera.localMapsRect;
			localMapsRectPrevious = waterCamera.localMapsRectPrevious;
			geometryType = waterCamera.geometryType;
		}

		/// <summary>
		/// Use this method to set a custom list of waters that should be rendered by this WaterCamera. Pass null to revert back to the default behaviour.
		/// </summary>
		/// <param name="waters"></param>
		public void SetCustomWaterRenderList(List<Water> waters)
		{
			customWaterRenderList = waters;
		}

		/// <summary>
		/// Fast and allocation free way to get a WaterCamera component attached to camera.
		/// </summary>
		/// <param name="camera"></param>
		/// <param name="forceAdd"></param>
		/// <returns></returns>
		public static WaterCamera GetWaterCamera(Camera camera, bool forceAdd = false)
		{
			WaterCamera waterCamera;

			if(!waterCamerasCache.TryGetValue(camera, out waterCamera))
			{
				waterCamera = camera.GetComponent<WaterCamera>();

				if(waterCamera != null)
					waterCamerasCache[camera] = waterCamera;
				else if(forceAdd)
					waterCamerasCache[camera] = camera.gameObject.AddComponent<WaterCamera>();
				else
					// ReSharper disable once RedundantAssignment
					waterCamerasCache[camera] = waterCamera = null;         // force null reference (Unity uses custom null checks)
			}

			return waterCamera;
		}

		private void RenderWaterDirect()
		{
			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

			for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
				waters[waterIndex].Renderer.Render(cameraComponent, geometryType);
		}

		public void RenderWaterWithShader(string commandName, RenderTexture target, Shader shader, Water water)
		{
			var commandBuffer = UtilityCommandBuffer;
			commandBuffer.Clear();
#if UNITY_EDITOR
			commandBuffer.name = commandName;
#endif
			commandBuffer.SetRenderTarget(target);
			
			water.Renderer.Render(cameraComponent, geometryType, commandBuffer, shader);

			GL.PushMatrix();
			GL.modelview = cameraComponent.worldToCameraMatrix;
			GL.LoadProjectionMatrix(cameraComponent.projectionMatrix);

			Graphics.ExecuteCommandBuffer(commandBuffer);

			GL.PopMatrix();
		}

		public void RenderWaterWithShader(string commandName, RenderTargetIdentifier[] targets, RenderTargetIdentifier depthTarget, Shader shader, Water water)
		{
			var commandBuffer = UtilityCommandBuffer;
			commandBuffer.Clear();
#if UNITY_EDITOR
			commandBuffer.name = commandName;
#endif
			commandBuffer.SetRenderTarget(targets, depthTarget);

			water.Renderer.Render(cameraComponent, geometryType, commandBuffer, shader);

			GL.PushMatrix();
			GL.modelview = cameraComponent.worldToCameraMatrix;
			GL.LoadProjectionMatrix(cameraComponent.projectionMatrix);

			Graphics.ExecuteCommandBuffer(commandBuffer);

			GL.PopMatrix();
		}

		public void RenderWaterWithShader(string commandName, RenderTexture target, Shader shader, bool surfaces, bool volumes, bool volumesTwoPass)
		{
			var commandBuffer = UtilityCommandBuffer;
			commandBuffer.Clear();
#if UNITY_EDITOR
			commandBuffer.name = commandName;
#endif
			commandBuffer.SetRenderTarget(target);

			AddWaterRenderCommands(commandBuffer, shader, surfaces, volumes, volumesTwoPass);

			GL.PushMatrix();
			GL.modelview = cameraComponent.worldToCameraMatrix;
			GL.LoadProjectionMatrix(cameraComponent.projectionMatrix);

			Graphics.ExecuteCommandBuffer(commandBuffer);

			GL.PopMatrix();
		}

		private void AddWaterRenderCommands(CommandBuffer commandBuffer, Shader shader, bool surfaces, bool volumes, bool volumesTwoPass)
		{
			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

			if(volumes)
			{
				for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
					waters[waterIndex].Renderer.RenderVolumes(commandBuffer, shader, volumesTwoPass);
			}

			if(surfaces)
			{
				for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
					waters[waterIndex].Renderer.Render(cameraComponent, geometryType, commandBuffer, shader);
			}
		}

		private void AddWaterMasksRenderCommands(CommandBuffer commandBuffer)
		{
			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;
			
			for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
				waters[waterIndex].Renderer.RenderMasks(commandBuffer);
		}

		private void RenderWaterEffects()
		{
			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

			for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
				waters[waterIndex].Renderer.RenderEffects(cameraComponent);
		}

		private void RenderWaterDepthBuffer(int baseEffectsWidth, int baseEffectsHeight)
		{
			if (renderMode == WaterRenderMode.ImageEffectDeferred)
			{
				baseEffectsWidth >>= 1;
				baseEffectsHeight >>= 1;
			}

			int depthRT = Shader.PropertyToID("_CameraDepthTexture2");
			int waterlessDepthRT = Shader.PropertyToID("_WaterlessDepthTexture");

			var depthBlitCopyMaterial = DepthBlitCopyMaterial;
			var commandBuffer = depthRenderCommands;

			if(commandBuffer == null)
				depthRenderCommands = commandBuffer = new CommandBuffer { name = "[PW Water] Render Depth" };

			commandBuffer.Clear();
			commandBuffer.GetTemporaryRT(waterDepthTextureId, baseEffectsWidth, baseEffectsHeight, waterDepthTextureFormat == RenderTextureFormat.Depth ? 32 : 16, baseEffectsQuality > 0.98f ? FilterMode.Point : FilterMode.Bilinear, waterDepthTextureFormat, RenderTextureReadWrite.Linear);
			commandBuffer.SetRenderTarget(waterDepthTextureId);
			commandBuffer.ClearRenderTarget(true, true, Color.white);
			AddWaterRenderCommands(commandBuffer, waterDepthShader, true, true, false);

			commandBuffer.GetTemporaryRT(waterlessDepthRT, pixelWidth, pixelHeight,
				blendedDepthTexturesFormat == RenderTextureFormat.Depth ? 32 : 0, FilterMode.Point, blendedDepthTexturesFormat,
				RenderTextureReadWrite.Linear);

			//if (!IsSceneViewCamera(cameraComponent))
			{
				commandBuffer.SetRenderTarget(waterlessDepthRT);
				commandBuffer.DrawMesh(PlayWay.Water.Internal.Quads.BipolarXInversedY, Matrix4x4.identity, depthBlitCopyMaterial, 0, blendedDepthTexturesFormat == RenderTextureFormat.Depth ? 3 : 0);
				//commandBuffer.Blit(BuiltinRenderTextureType.None, waterlessDepthRT, depthMixerMaterial, 0);
			}
			/*else
			{
				commandBuffer.SetRenderTarget(waterlessDepthRT);
				commandBuffer.ClearRenderTarget(true, true, new Color(10000.0f, 10000.0f, 10000.0f, 10000.0f));
			}*/

			commandBuffer.GetTemporaryRT(depthRT, pixelWidth, pixelHeight,
				blendedDepthTexturesFormat == RenderTextureFormat.Depth ? 32 : 0, FilterMode.Point, blendedDepthTexturesFormat,
				RenderTextureReadWrite.Linear);
			commandBuffer.SetRenderTarget(depthRT);
			commandBuffer.ClearRenderTarget(true, true, Color.white);
			commandBuffer.DrawMesh(PlayWay.Water.Internal.Quads.BipolarXInversedY, Matrix4x4.identity, depthBlitCopyMaterial, 0, blendedDepthTexturesFormat == RenderTextureFormat.Depth ? 4 : 1);
			//commandBuffer.Blit(BuiltinRenderTextureType.None, depthRT, depthMixerMaterial, 1);
			commandBuffer.SetGlobalTexture("_CameraDepthTexture", depthRT);

			cameraComponent.RemoveCommandBuffer(singlePassStereoRendering ? CameraEvent.BeforeForwardOpaque : CameraEvent.AfterDepthTexture, commandBuffer);
			cameraComponent.RemoveCommandBuffer(CameraEvent.BeforeLighting, commandBuffer);
			cameraComponent.AddCommandBuffer(cameraComponent.actualRenderingPath == RenderingPath.Forward ? (singlePassStereoRendering ? CameraEvent.BeforeForwardOpaque : CameraEvent.AfterDepthTexture) : CameraEvent.BeforeLighting, commandBuffer);
		}

		private void RenderWaterMasks(int baseEffectsWidth, int baseEffectsHeight)
		{
			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;
			int numWaters = waters.Count;

			bool hasSubtractiveVolumes = false;
			bool hasAdditiveVolumes = false;
			bool hasFlatMasks = false;

			for(int i = numWaters - 1; i >= 0; --i)
				waters[i].Renderer.OnSharedSubtractiveMaskRender(ref hasSubtractiveVolumes, ref hasAdditiveVolumes, ref hasFlatMasks);

			var commandBuffer = volumeRenderCommands;

			if (commandBuffer == null)
				volumeRenderCommands = commandBuffer = new CommandBuffer { name = "[PW Water] Render Volumes and Masks" };

			commandBuffer.Clear();

			var effectCamera = EffectsCamera;
			var effectWaterCamera = effectCamera.GetComponent<WaterCamera>();
			effectCamera.transform.position = transform.position;
			effectCamera.transform.rotation = transform.rotation;
			effectCamera.projectionMatrix = cameraComponent.projectionMatrix;
			//effectCamera.CopyFrom(thisCamera);

			if(hasSubtractiveVolumes || hasFlatMasks)
			{
				commandBuffer.GetTemporaryRT(subtractiveMaskId, baseEffectsWidth, baseEffectsHeight, 24, baseEffectsQuality > 0.98f ? FilterMode.Point : FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
				commandBuffer.SetRenderTarget(subtractiveMaskId);
				commandBuffer.ClearRenderTarget(true, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));

				if(hasSubtractiveVolumes)
				{
					effectCamera.cullingMask = 1 << WaterProjectSettings.Instance.WaterLayer;
					effectWaterCamera.AddWaterRenderCommands(commandBuffer, isInsideSubtractiveVolume ? volumeFrontShader : volumeFrontFastShader, false, true, isInsideSubtractiveVolume);
					effectWaterCamera.AddWaterRenderCommands(commandBuffer, volumeBackShader, false, true, false);
				}

				if(hasFlatMasks && renderFlatMasks)
				{
					effectCamera.cullingMask = 1 << WaterProjectSettings.Instance.WaterTempLayer;
					effectWaterCamera.AddWaterMasksRenderCommands(commandBuffer);
				}
			}
			else
			{
				Shader.SetGlobalTexture(subtractiveMaskId, DefaultTextures.BlackTexture);
			}

			if(hasAdditiveVolumes)
			{
				for(int i = numWaters - 1; i >= 0; --i)
					waters[i].Renderer.OnSharedMaskAdditiveRender();
				
				effectCamera.cullingMask = (1 << WaterProjectSettings.Instance.WaterLayer);
				commandBuffer.GetTemporaryRT(additiveMaskId, baseEffectsWidth, baseEffectsHeight, 24, baseEffectsQuality > 0.98f ? FilterMode.Point : FilterMode.Bilinear, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
				commandBuffer.SetRenderTarget(additiveMaskId);
				commandBuffer.ClearRenderTarget(true, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));
				effectWaterCamera.AddWaterRenderCommands(commandBuffer, isInsideAdditiveVolume ? volumeFrontShader : volumeFrontFastShader, false, true, isInsideAdditiveVolume);
				effectWaterCamera.AddWaterRenderCommands(commandBuffer, volumeBackShader, false, true, false);
			}
			else
			{
				Shader.SetGlobalTexture(additiveMaskId, DefaultTextures.BlackTexture);
			}

			if (commandBuffer.sizeInBytes != 0)
			{
				var evt = cameraComponent.actualRenderingPath == RenderingPath.Forward ? CameraEvent.BeforeForwardOpaque : CameraEvent.BeforeGBuffer;
				cameraComponent.RemoveCommandBuffer(evt, commandBuffer);
				cameraComponent.AddCommandBuffer(evt, commandBuffer);
			}

			for(int i = numWaters - 1; i >= 0; --i)
				waters[i].Renderer.OnSharedMaskPostRender();
		}

		private static void SetBlankWaterMasks()
		{
			/*var blackTexture = DefaultTextures.BlackTexture;
			var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

			for (int i = waters.Count - 1; i >= 0; --i)
			{
				var water = waters[i];
				water.Renderer.PropertyBlock.SetTexture(subtractiveMaskId, blackTexture);
				water.Renderer.PropertyBlock.SetTexture(additiveMaskId, blackTexture);
			}*/
		}
		
		private void RemoveDepthRenderingCommands()
		{
			if(depthRenderCommands != null)
			{
				cameraComponent.RemoveCommandBuffer(singlePassStereoRendering ? CameraEvent.BeforeForwardOpaque : CameraEvent.AfterDepthTexture, depthRenderCommands);
				cameraComponent.RemoveCommandBuffer(CameraEvent.BeforeLighting, depthRenderCommands);
				depthRenderCommands.Dispose();
				depthRenderCommands = null;
			}

			if(cleanUpCommands != null)
			{
				cameraComponent.RemoveCommandBuffer(CameraEvent.AfterEverything, cleanUpCommands);
				cleanUpCommands.Dispose();
				cleanUpCommands = null;
			}
		}

		#region ImageEffectRenderModes

		private Camera waterRenderCamera;
		private CommandBuffer imageEffectCommands;
		private RenderTexture depthTex;
		private RenderTexture deferredRenderTarget;
		private Material gbuffer0MixMaterial, gbuffer123MixMaterial, finalColorMixMaterial;
		private static int gbufferId0, gbufferId1, gbufferId2, gbufferId3, refractTexId, waterlessDepthId;
		
		internal void OnRenderImageCallback(RenderTexture source, RenderTexture destination)
		{
			if(renderMode != WaterRenderMode.DefaultQueue)
			{
				var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

				for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
					waters[waterIndex].Volume.EnableRenderers();
			}

			switch(renderMode)
			{
				case WaterRenderMode.ImageEffectForward:
				{
					RenderWaterForward(source);
					Graphics.Blit(source, destination);
					break;
				}

				case WaterRenderMode.ImageEffectDeferred:
				{
#if UNITY_EDITOR && UNITY_5_5
					// that takes care of this warning: "OnRenderImage() possibly didn't write anything to the destination texture!"
					Graphics.SetRenderTarget(destination);
					GL.Clear(true, true, new Color(0.0f, 0.0f, 0.0f, 0.0f));
#endif

					var deferredRenderTarget = RenderTexture.GetTemporary(Mathf.RoundToInt(cameraComponent.pixelWidth * superSampling) + 1, Mathf.RoundToInt(cameraComponent.pixelHeight * superSampling), 16, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);      // get a buffer that is slightly larger to ensure that this camera buffers won't be used later this frame
					deferredRenderTarget.filterMode = FilterMode.Point;
					source.filterMode = FilterMode.Point;
					Shader.SetGlobalTexture(refractTexId, source);
					RenderWaterDeferred(deferredRenderTarget, destination);
					RenderTexture.ReleaseTemporary(deferredRenderTarget);
					break;
				}

				default:
				{
					Graphics.Blit(source, destination);
					break;
				}
			}

			if(renderMode != WaterRenderMode.DefaultQueue)
			{
				var waters = customWaterRenderList ?? WaterGlobals.Instance.Waters;

				for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
					waters[waterIndex].Volume.DisableRenderers();
			}
		}

		protected void Update()
		{
			switch(renderMode)
			{
				case WaterRenderMode.ImageEffectDeferred:
				{
					ReleaseImageEffectTemporaryTextures();
					break;
				}
			}
		}

		private void ReleaseImageEffectTemporaryTextures()
		{
			if(depthTex != null)
			{
				RenderTexture.ReleaseTemporary(depthTex);
				depthTex = null;
			}

			if(deferredRenderTarget != null)
			{
				RenderTexture.ReleaseTemporary(deferredRenderTarget);
				deferredRenderTarget = null;
			}
		}

		private void AddUtilityCommands()
		{
			if (imageEffectCommands == null && renderMode == WaterRenderMode.ImageEffectDeferred)
			{
				imageEffectCommands = new CommandBuffer {name = "[PW Water] Set Buffers"};
				imageEffectCommands.SetGlobalTexture(gbufferId0, BuiltinRenderTextureType.GBuffer0);
				imageEffectCommands.SetGlobalTexture(gbufferId1, BuiltinRenderTextureType.GBuffer1);
				imageEffectCommands.SetGlobalTexture(gbufferId2, BuiltinRenderTextureType.GBuffer2);
				imageEffectCommands.SetGlobalTexture(gbufferId3, BuiltinRenderTextureType.Reflections);
				imageEffectCommands.SetGlobalTexture(waterlessDepthId, BuiltinRenderTextureType.ResolvedDepth);

				cameraComponent.RemoveCommandBuffer(CameraEvent.AfterLighting, imageEffectCommands);
				cameraComponent.AddCommandBuffer(CameraEvent.AfterLighting, imageEffectCommands);
			}
		}

		private void RemoveUtilityCommands()
		{
			RemoveCommandBuffer(CameraEvent.AfterLighting, "[PW Water] Set Buffers");

			if(imageEffectCommands != null)
			{
				imageEffectCommands.Dispose();
				imageEffectCommands = null;
			}
		}

		/// <summary>
		/// Removes a command buffer by name. It's the most reliable way to remove a command buffer as references may be cleared in the editor.
		/// </summary>
		/// <param name="cameraEvent"></param>
		/// <param name="name"></param>
		private void RemoveCommandBuffer(CameraEvent cameraEvent, string name)
		{
			var buffers = cameraComponent.GetCommandBuffers(cameraEvent);

			for (int i = buffers.Length - 1; i >= 0; --i)
			{
				if (buffers[i].name == name)
				{
					cameraComponent.RemoveCommandBuffer(cameraEvent, buffers[i]);
					return;
				}
			}
		}

		private void RenderWaterForward(RenderTexture target)
		{
			var waterRenderCamera = WaterRenderCamera;
			waterRenderCamera.CopyFrom(cameraComponent);
			
			var effectWaterCamera = waterRenderCamera.GetComponent<WaterCamera>();
			effectWaterCamera.CopyFrom(this);

			waterRenderCamera.enabled = false;
			waterRenderCamera.clearFlags = CameraClearFlags.Nothing;
			waterRenderCamera.depthTextureMode = DepthTextureMode.None;
			waterRenderCamera.renderingPath = RenderingPath.Forward;
#if UNITY_5_6
			waterRenderCamera.allowHDR = true;
#else
			waterRenderCamera.hdr = true;
#endif
			waterRenderCamera.targetTexture = target;
			waterRenderCamera.cullingMask = (1 << WaterProjectSettings.Instance.WaterLayer);
			waterRenderCamera.Render();
			waterRenderCamera.targetTexture = null;
		}

		private void RenderWaterDeferred(RenderTexture temp, RenderTexture target)
		{
			var waterRenderCamera = WaterRenderCamera;
			waterRenderCamera.CopyFrom(cameraComponent);

			var mainWater = MainWater;

			if(renderMode == WaterRenderMode.ImageEffectDeferred)
			{
				finalColorMixMaterial.SetMatrix(unityMatrixVPInverseId, Matrix4x4.Inverse(GL.GetGPUProjectionMatrix(cameraComponent.projectionMatrix, true) * cameraComponent.worldToCameraMatrix));
				gbuffer123MixMaterial.SetFloat(depthClipMultiplierId, -1.0f);
				
				if(gbuffer0Tex == null)
				{
					gbuffer0Tex = RenderTexture.GetTemporary(cameraComponent.pixelWidth + 1, cameraComponent.pixelHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
					gbuffer0Tex.filterMode = FilterMode.Point;
				}

				if (depthTex2 == null)
				{
					depthTex2 = RenderTexture.GetTemporary(cameraComponent.pixelWidth + 1, cameraComponent.pixelHeight, blendedDepthTexturesFormat == RenderTextureFormat.Depth ? 32 : 0, blendedDepthTexturesFormat, RenderTextureReadWrite.Linear);
					depthTex2.filterMode = FilterMode.Point;
				}

				var utilityCommandBuffer = UtilityCommandBuffer;
				utilityCommandBuffer.Clear();
				utilityCommandBuffer.name = "[PW Water] Blend Deferred Results";
				
				// depth
				var depthBlitCopyMaterial = DepthBlitCopyMaterial;
				utilityCommandBuffer.SetRenderTarget(depthTex2);
				utilityCommandBuffer.DrawMesh(PlayWay.Water.Internal.Quads.BipolarXInversedY, Matrix4x4.identity, depthBlitCopyMaterial, 0, blendedDepthTexturesFormat == RenderTextureFormat.Depth ? 5 : 2);
				
				// gbuffer 0, 1, 2, 3
				utilityCommandBuffer.Blit(BuiltinRenderTextureType.GBuffer0, gbuffer0Tex, gbuffer0MixMaterial, 0);
				utilityCommandBuffer.SetRenderTarget(deferredTargets, BuiltinRenderTextureType.Reflections);
				utilityCommandBuffer.DrawMesh(PlayWay.Water.Internal.Quads.BipolarXY, Matrix4x4.identity, gbuffer123MixMaterial, 0);
				
				// final color
				utilityCommandBuffer.SetRenderTarget(target);
				utilityCommandBuffer.SetGlobalTexture("_WaterColorTex", BuiltinRenderTextureType.CameraTarget);
				utilityCommandBuffer.DrawMesh(PlayWay.Water.Internal.Quads.BipolarXInversedY, Matrix4x4.identity, finalColorMixMaterial, 0, 0, mainWater != null ? mainWater.Renderer.PropertyBlock : null);

				utilityCommandBuffer.SetGlobalTexture("_CameraDepthTexture", depthTex2);
				utilityCommandBuffer.SetGlobalTexture("_CameraGBufferTexture0", gbuffer0Tex);
				utilityCommandBuffer.SetGlobalTexture("_CameraGBufferTexture1", BuiltinRenderTextureType.GBuffer1);
				utilityCommandBuffer.SetGlobalTexture("_CameraGBufferTexture2", BuiltinRenderTextureType.GBuffer2);
				utilityCommandBuffer.SetGlobalTexture("_CameraGBufferTexture3", BuiltinRenderTextureType.Reflections);
				utilityCommandBuffer.SetGlobalTexture("_CameraReflectionsTexture", BuiltinRenderTextureType.Reflections);

				waterRenderCamera.AddCommandBuffer(CameraEvent.AfterEverything, utilityCommandBuffer);
			}

			var originalDeferredReflections = GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredReflections);
			var originalDeferredShading = GraphicsSettings.GetCustomShader(BuiltinShaderType.DeferredShading);
			var originalShaderModeReflections = GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredReflections);
			var originalShaderModeShading = GraphicsSettings.GetShaderMode(BuiltinShaderType.DeferredShading);

			GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredReflections, deferredReflections);
			GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, deferredShading);
			GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredReflections, BuiltinShaderMode.UseCustom);
			GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, BuiltinShaderMode.UseCustom);
			
			if(mainWater != null)
				Shader.SetGlobalVector("_MainWaterWrapSubsurfaceScatteringPack", mainWater.Renderer.PropertyBlock.GetVector("_WrapSubsurfaceScatteringPack"));

			var effectWaterCamera = waterRenderCamera.GetComponent<WaterCamera>();
			effectWaterCamera.CopyFrom(this);
			
			waterRenderCamera.enabled = false;
			waterRenderCamera.clearFlags = CameraClearFlags.Color;
			waterRenderCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
			waterRenderCamera.depthTextureMode = DepthTextureMode.None;
			waterRenderCamera.renderingPath = RenderingPath.DeferredShading;
#if UNITY_5_6
			waterRenderCamera.allowHDR = true;
#else
			waterRenderCamera.hdr = true;
#endif
			waterRenderCamera.targetTexture = temp;
			waterRenderCamera.cullingMask = (1 << WaterProjectSettings.Instance.WaterLayer);
			waterRenderCamera.Render();
			waterRenderCamera.targetTexture = null;
			
			GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredReflections, originalDeferredReflections);
			GraphicsSettings.SetCustomShader(BuiltinShaderType.DeferredShading, originalDeferredShading);
			GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredReflections, originalShaderModeReflections);
			GraphicsSettings.SetShaderMode(BuiltinShaderType.DeferredShading, originalShaderModeShading);

			Shader.SetGlobalTexture("_CameraDepthTexture", depthTex2);

			if(renderMode == WaterRenderMode.ImageEffectDeferred)
				waterRenderCamera.RemoveCommandBuffer(CameraEvent.AfterEverything, WaterCamera.utilityCommandBuffer);
		}

		#endregion

		private void EnableEffects()
		{
			if(waterCameraType != CameraType.Normal)
				return;

			pixelWidth = Mathf.RoundToInt(cameraComponent.pixelWidth * baseEffectsQuality);
			pixelHeight = Mathf.RoundToInt(cameraComponent.pixelHeight * baseEffectsQuality);

			if (singlePassStereoRendering)
			{
				pixelWidth <<= 1;
				pixelWidth += 48;           // works at least for HTC Vive / there seems to be no way to determine a single-pass camera resolution
			}

			effectsEnabled = true;
			
			if(renderWaterDepth || renderVolumes)
				cameraComponent.depthTextureMode |= DepthTextureMode.Depth;
		}

		private void DisableEffects()
		{
			effectsEnabled = false;
			RemoveDepthRenderingCommands();
		}

		private static bool IsWaterPossiblyVisible()
		{
#if UNITY_EDITOR
			if(!Application.isPlaying)
				return true;
#endif

			var waters = WaterGlobals.Instance.Waters;
			return waters.Count != 0;
		}

		protected Camera CreateEffectsCamera(CameraType type)
		{
			var effectCameraGo = new GameObject(name + " Water Effects Camera") {hideFlags = HideFlags.HideAndDontSave};

			var effectCamera = effectCameraGo.AddComponent<Camera>();
			effectCamera.enabled = false;
			effectCamera.useOcclusionCulling = false;

			var effectWaterCamera = effectCameraGo.AddComponent<WaterCamera>();
			effectWaterCamera.waterCameraType = type;
			effectWaterCamera.mainCamera = cameraComponent;
			effectWaterCamera.baseCamera = this;
			effectWaterCamera.waterDepthShader = waterDepthShader;

			enabledWaterCameras.Remove(effectWaterCamera);

			return effectCamera;
		}

		private void RenderShadowEnforcers()
		{
			if(shadowsEnforcerMesh == null)
			{
				shadowsEnforcerMesh = new Mesh
				{
					name = "Water Shadow Enforcer",
					hideFlags = HideFlags.DontSave,
					vertices = new Vector3[4]
				};
				shadowsEnforcerMesh.SetIndices(new[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
				shadowsEnforcerMesh.UploadMeshData(true);

				shadowsEnforcerMaterial = new Material(shadowEnforcerShader) {hideFlags = HideFlags.DontSave};
			}

			var bounds = new Bounds();

			float distance = QualitySettings.shadowDistance;
			Vector3 a = cameraComponent.ViewportPointToRay(new Vector3(shadowedWaterRect.xMin, shadowedWaterRect.yMin, 1.0f)).GetPoint(distance * 1.5f);
			Vector3 b = cameraComponent.ViewportPointToRay(new Vector3(shadowedWaterRect.xMax, shadowedWaterRect.yMax, 1.0f)).GetPoint(distance * 1.5f);
			SetBoundsMinMaxComponentWise(ref bounds, a, b);
			bounds.Encapsulate(cameraComponent.ViewportPointToRay(new Vector3(shadowedWaterRect.xMin, shadowedWaterRect.yMax, 1.0f)).GetPoint(distance * 0.3f));
			bounds.Encapsulate(cameraComponent.ViewportPointToRay(new Vector3(shadowedWaterRect.xMax, shadowedWaterRect.yMin, 1.0f)).GetPoint(distance * 0.3f));
			shadowsEnforcerMesh.bounds = bounds;

			Graphics.DrawMesh(shadowsEnforcerMesh, Matrix4x4.identity, shadowsEnforcerMaterial, 0);
		}

		private void SetBoundsMinMaxComponentWise(ref Bounds bounds, Vector3 a, Vector3 b)
		{
			if(a.x > b.x)
			{
				float t = b.x;
				b.x = a.x;
				a.x = t;
			}

			if(a.y > b.y)
			{
				float t = b.y;
				b.y = a.y;
				a.y = t;
			}

			if(a.z > b.z)
			{
				float t = b.z;
				b.z = a.z;
				a.z = t;
			}

			bounds.SetMinMax(a, b);
		}

		private void PrepareToRender()
		{
			// reset shadowed water rect
			shadowedWaterRect = new Rect(1.0f, 1.0f, -1.0f, -1.0f);

#if UNITY_EDITOR
			//if(IsSceneViewCamera(cameraComponent))
			//	return;                         // don't do any of the following stuff for editor cameras
#endif

			// find containing water
			float waterEnterTolerance = 1.0f + Mathf.Max(0.5f, cameraComponent.nearClipPlane) * Mathf.Tan(Mathf.Max(16.0f, cameraComponent.fieldOfView) * 0.5f * Mathf.Deg2Rad) * 3.0f;
			var newWater = Water.FindWater(transform.position, waterEnterTolerance, customWaterRenderList, out isInsideSubtractiveVolume, out isInsideAdditiveVolume);

			if(newWater != containingWater)
			{
				if(containingWater != null && submersionState != SubmersionState.None)
				{
					submersionState = SubmersionState.None;
					SubmersionStateChanged.Invoke(this);
				}

				containingWater = newWater;
				submersionState = SubmersionState.None;

				if(waterSample != null)
				{
					waterSample.Stop();
					waterSample = null;
				}

				if(newWater != null && newWater.Volume.Boundless)
				{
					waterSample = new WaterSample(containingWater, WaterSample.DisplacementMode.Height, 0.4f);
					waterSample.Start(transform.position);
				}
			}

			// determine submersion state
			SubmersionState newSubmersionState;

			if(waterSample != null)
			{
				waterLevel = waterSample.GetAndReset(transform.position).y;

				if(transform.position.y - waterEnterTolerance < waterLevel)
					newSubmersionState = transform.position.y + waterEnterTolerance < waterLevel ? SubmersionState.Full : SubmersionState.Partial;
				else
					newSubmersionState = SubmersionState.None;
			}
			else
			{
				newSubmersionState = containingWater != null ? SubmersionState.Partial : SubmersionState.None;          // for non-boundless water always use Partial state as determining this would be too costly
			}

			if(newSubmersionState != submersionState)
			{
				submersionState = newSubmersionState;
				SubmersionStateChanged.Invoke(this);
			}
		}

		private static void SetFallbackTextures()
		{
			Shader.SetGlobalTexture(underwaterMaskId, DefaultTextures.BlackTexture);
			Shader.SetGlobalTexture(displacementsMaskId, DefaultTextures.WhiteTexture);
		}

		private void ToggleEffects()
		{
			if(!effectsEnabled)
			{
				if(IsWaterPossiblyVisible())
					EnableEffects();
			}
			else if(!IsWaterPossiblyVisible())
				DisableEffects();

			int pixelWidth = Mathf.RoundToInt(cameraComponent.pixelWidth * baseEffectsQuality);
			int pixelHeight = Mathf.RoundToInt(cameraComponent.pixelHeight * baseEffectsQuality);

			if(effectsEnabled && (pixelWidth != this.pixelWidth || pixelHeight != this.pixelHeight))
			{
				DisableEffects();
				EnableEffects();

				if(RenderTargetResized != null)
					RenderTargetResized(this);
            }
		}

		private void SetPlaneProjectorMatrix()
		{
			Vector3 position = transform.position;
			Vector3 forward = transform.forward;
			Vector3 forwardFlat = new Vector3(forward.x, 0.0f, forward.z);
			Vector3 lookAtFinal = new Vector3();

			var waters = WaterGlobals.Instance.DynamicWaters;

			for(int i = waters.Count - 1; i >= 0; --i)
			{
				var water = waters[i];

				// move projector out of boundary
				float verticalOffset = water.transform.position.y;

				if(position.y > verticalOffset)
				{
					float t = verticalOffset + waters[i].MaxVerticalDisplacement;

					if(position.y < t)
						position.y = t;         // position += forward*(forward.y > 0.0f ? t - position.y : position.y - t);

					if(forward.y > -0.002f)
					{
						forward.y = -0.002f;
						forward.Normalize();
					}
				}
				else
				{
					float t = verticalOffset - waters[i].MaxVerticalDisplacement;

					if(position.y > t)
						position.y = t;         // position += forward*(forward.y > 0.0f ? t - position.y : position.y - t);

					if(forward.y < 0.002f)
					{
						forward.y = 0.002f;
						forward.Normalize();
					}
				}
				
				if(i == waters.Count - 1)
				{
					float lookAtDist = (verticalOffset - transform.position.y) / forward.y;

					lookAtFinal = position + new Vector3(forward.x, 0.0f, forward.z) * ((lookAtDist - waters[i].MaxHorizontalDisplacement) * 0.85f);
					lookAtFinal.y = verticalOffset;

					position -= forwardFlat * (waters[i].MaxHorizontalDisplacement * (1.0f + Mathf.Tan(cameraComponent.fieldOfView * Mathf.Deg2Rad * 0.5f)));
                }
            }

			var planeProjectorCamera = PlaneProjectorCamera;

			Shader.SetGlobalMatrix("_WaterProjectorPreviousVP", lastPlaneProjectorMatrix);

			planeProjectorCamera.CopyFrom(cameraComponent);
			planeProjectorCamera.renderingPath = RenderingPath.Forward;
			planeProjectorCamera.transform.position = position;
			planeProjectorCamera.transform.LookAt(lookAtFinal, transform.up);
			planeProjectorCamera.ResetProjectionMatrix();

			Vector2 lookAtFinal2D = new Vector2(lookAtFinal.x, lookAtFinal.z);
			Matrix4x4 projectorMatrix = planeProjectorCamera.projectionMatrix * planeProjectorCamera.worldToCameraMatrix;
			Vector4 bounds = new Vector4(10000.0f, 10000.0f, -10000.0f, -10000.0f);

			float farClipPlane = cameraComponent.farClipPlane;
			Vector3 cameraPosition = transform.position;
			Vector3 a = cameraComponent.ViewportToWorldPoint(new Vector3(0.0f, 0.0f, farClipPlane));
			Vector3 b = cameraComponent.ViewportToWorldPoint(new Vector3(1.0f, 0.0f, farClipPlane));
			Vector3 c = cameraComponent.ViewportToWorldPoint(new Vector3(0.0f, 1.0f, farClipPlane));
			Vector3 d = cameraComponent.ViewportToWorldPoint(new Vector3(1.0f, 1.0f, farClipPlane));
			
			float projectionEdgeLen = Vector3.Distance(cameraPosition, a);
			float farClipPlaneWidth = Vector3.Distance(a, b);
			float farClipPlaneHeight = Vector3.Distance(a, c);

			IntersectionToBounds(cameraPosition, a, 0.0f, projectionEdgeLen, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(cameraPosition, b, 0.0f, projectionEdgeLen, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(cameraPosition, c, 0.0f, projectionEdgeLen, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(cameraPosition, d, 0.0f, projectionEdgeLen, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(a, b, 0.0f, farClipPlaneWidth, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(c, d, 0.0f, farClipPlaneWidth, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(a, c, 0.0f, farClipPlaneHeight, lookAtFinal2D, ref projectorMatrix, ref bounds);
			IntersectionToBounds(b, d, 0.0f, farClipPlaneHeight, lookAtFinal2D, ref projectorMatrix, ref bounds);
			
			//Debug.Log(bounds);

			// temporary workaround for close-up issues
			if (bounds.y > -0.7f) bounds.y = -0.7f;
			if (bounds.w < 0.7f) bounds.w = 0.7f;

			Matrix4x4 scaleMatrix = Matrix4x4.identity;
			scaleMatrix.m00 = 2.0f / (bounds.z - bounds.x);
			scaleMatrix.m11 = 2.0f / (bounds.w - bounds.y);
			scaleMatrix.m03 = -bounds.x * scaleMatrix.m00 - 1.0f;
			scaleMatrix.m13 = -bounds.y * scaleMatrix.m11 - 1.0f;
			
            planeProjectorCamera.projectionMatrix = scaleMatrix * planeProjectorCamera.projectionMatrix;
			lastPlaneProjectorMatrix = GL.GetGPUProjectionMatrix(planeProjectorCamera.projectionMatrix, true) * planeProjectorCamera.worldToCameraMatrix;

			Shader.SetGlobalMatrix("_WaterProjectorVP", lastPlaneProjectorMatrix);
		}

		private static void IntersectionToBounds(Vector3 a, Vector3 b, float min, float max, Vector2 center, ref Matrix4x4 projectorMatrix, ref Vector4 bounds)
		{
			Vector3 dir = b - a;
			dir.Normalize();

			var waters = WaterGlobals.Instance.DynamicWaters;

			for(int i = waters.Count - 1; i >= 0; --i)
			{
				float offset = waters[i].transform.position.y;
				float maxHorizontalDisplacement = waters[i].MaxHorizontalDisplacement;
				float maxVerticalDisplacement = waters[i].MaxVerticalDisplacement;

				// top
				float p = (offset + maxVerticalDisplacement - a.y) / dir.y;

				if(p >= min && p <= max)
				{
					Vector3 intersection = new Vector3(a.x + dir.x * p, offset, a.z + dir.z * p);

					Vector2 fromCenter = new Vector2(intersection.x - center.x, intersection.z - center.y);
					float len = maxHorizontalDisplacement / Mathf.Sqrt(fromCenter.x * fromCenter.x + fromCenter.y * fromCenter.y);
					intersection.x += fromCenter.x * len;
					intersection.z += fromCenter.y * len;

					Vector3 projected = projectorMatrix.MultiplyPoint(intersection);

					if(projected.z >= 0.0f)
					{
						if(projected.x < bounds.x) bounds.x = projected.x;
						if(projected.x > bounds.z) bounds.z = projected.x;
						if(projected.y < bounds.y) bounds.y = projected.y;
						if(projected.y > bounds.w) bounds.w = projected.y;
					}
				}

				p = (offset - maxVerticalDisplacement - a.y) / dir.y;

				// bottom
				if(p >= min && p <= max)
				{
					Vector3 intersection2 = new Vector3(a.x + dir.x * p, offset, a.z + dir.z * p);

					Vector2 fromCenter = new Vector2(intersection2.x - center.x, intersection2.z - center.y);
					float len = maxHorizontalDisplacement / Mathf.Sqrt(fromCenter.x * fromCenter.x + fromCenter.y * fromCenter.y);
					intersection2.x += fromCenter.x * len;
					intersection2.z += fromCenter.y * len;

					Vector3 projected2 = projectorMatrix.MultiplyPoint(intersection2);

					if(projected2.z >= 0.0f)
					{
						if(projected2.x < bounds.x) bounds.x = projected2.x;
						if(projected2.x > bounds.z) bounds.z = projected2.x;
						if(projected2.y < bounds.y) bounds.y = projected2.y;
						if(projected2.y > bounds.w) bounds.w = projected2.y;
					}
				}
			}
        }

		private void SetLocalMapCoordinates()
		{
			int resolution = Mathf.NextPowerOfTwo((cameraComponent.pixelWidth + cameraComponent.pixelHeight) >> 1);
			float maxHeight = 0.0f;
			float maxWaterLevel = 0.0f;

			var waters = WaterGlobals.Instance.Waters;

			for(int waterIndex = waters.Count - 1; waterIndex >= 0; --waterIndex)
			{
				var water = waters[waterIndex];
				maxHeight += water.MaxVerticalDisplacement;

				float posY = water.transform.position.y;
				if(maxWaterLevel < posY)
					maxWaterLevel = posY;
			}

			// place camera
			Vector3 thisCameraPosition = cameraComponent.transform.position;
			Vector3 thisCameraForward = cameraComponent.transform.forward;
			float forwardFactor = Mathf.Min(1.0f, thisCameraForward.y + 1.0f);

			float size1 = Mathf.Abs(thisCameraPosition.y) * (1.0f + 7.0f * Mathf.Sqrt(forwardFactor));
			float size2 = maxHeight * 2.5f;
			float size = size1 > size2 ? size1 : size2;

			if(size < 20.0f)
				size = 20.0f;

			Vector3 effectCameraPosition = new Vector3(thisCameraPosition.x + thisCameraForward.x * size * 0.4f, 0.0f, thisCameraPosition.z + thisCameraForward.z * size * 0.4f);

			localMapsRectPrevious = localMapsRect;

			float halfPixelSize = size / resolution;
			localMapsRect = new Rect((effectCameraPosition.x - size) + halfPixelSize, (effectCameraPosition.z - size) + halfPixelSize, 2.0f * size, 2.0f * size);

			float invWidthPrevious = 1.0f/localMapsRectPrevious.width;
			Shader.SetGlobalVector(localMapsCoordsPreviousId, new Vector4(-localMapsRectPrevious.xMin * invWidthPrevious, -localMapsRectPrevious.yMin * invWidthPrevious, invWidthPrevious, localMapsRectPrevious.width));

			float invWidth = 1.0f/localMapsRect.width;
			Shader.SetGlobalVector(localMapsCoordsId, new Vector4(-localMapsRect.xMin * invWidth, -localMapsRect.yMin * invWidth, invWidth, localMapsRect.width));
		}

		private void InitializeStaticFields()
		{
			waterDepthTextureId = Shader.PropertyToID("_WaterDepthTexture");
			underwaterMaskId = Shader.PropertyToID("_UnderwaterMask");
			additiveMaskId = Shader.PropertyToID("_AdditiveMask");
			subtractiveMaskId = Shader.PropertyToID("_SubtractiveMask");
			displacementsMaskId = Shader.PropertyToID("_DisplacementsMask");
			unityMatrixVPInverseId = Shader.PropertyToID("UNITY_MATRIX_VP_INVERSE");
			depthClipMultiplierId = Shader.PropertyToID("_DepthClipMultiplier");
			localMapsCoordsId = Shader.PropertyToID("_LocalMapsCoords");
			localMapsCoordsPreviousId = Shader.PropertyToID("_LocalMapsCoordsPrevious");
			gbufferId0 = Shader.PropertyToID("_CameraGBufferTextureOriginal0");
			gbufferId1 = Shader.PropertyToID("_CameraGBufferTextureOriginal1");
			gbufferId2 = Shader.PropertyToID("_CameraGBufferTextureOriginal2");
			gbufferId3 = Shader.PropertyToID("_CameraGBufferTextureOriginal3");
			refractTexId = Shader.PropertyToID("_RefractionTex");
			waterlessDepthId = Shader.PropertyToID("_WaterlessDepthTexture");

			OnValidate();
		}

		public static bool IsSceneViewCamera(Camera camera)
		{
#if UNITY_EDITOR && !UNITY_5_0 && !UNITY_5_1 && !UNITY_5_2             // use 5.3 API for this
			return camera.cameraType == UnityEngine.CameraType.SceneView;
#elif UNITY_EDITOR                                                     // fallback
			var sceneViews = UnityEditor.SceneView.sceneViews;
			int numSceneViews = sceneViews.Count;

			for(int i = 0; i < numSceneViews; ++i)
			{
				if(((UnityEditor.SceneView)sceneViews[i]).camera == camera)
					return true;
			}

			return false;
#else
			return false;
#endif
		}

		[System.Serializable]
		public class WaterCameraEvent : UnityEvent<WaterCamera> { }
	}
}
