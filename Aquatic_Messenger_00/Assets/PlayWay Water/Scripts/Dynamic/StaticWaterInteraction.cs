using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	public sealed class StaticWaterInteraction : MonoBehaviour, IWaterShore, IWaterInteraction
	{
		[HideInInspector]
		[SerializeField]
		private Shader maskGenerateShader;

		[HideInInspector]
		[SerializeField]
		private Shader maskDisplayShader;

		[HideInInspector]
		[SerializeField]
		private Shader heightMapperShader;

		[HideInInspector]
		[SerializeField]
		private Shader heightMapperShaderAlt;

		[Tooltip("Specifies a distance from the shore over which a water gets one meter deeper (value of 50 means that water has a depth of 1m at a distance of 50m from the shore).")]
		[Range(0.001f, 80.0f)]
		[SerializeField]
		private float shoreSmoothness = 50.0f;

		[Tooltip("If set to true, geometry that floats above water is correctly ignored.\n\nUse for objects that are closed and have faces at the bottom like basic primitives and most custom meshes, but not terrain.")]
		[SerializeField]
		private bool hasBottomFaces;

		[SerializeField]
		private UnderwaterAreasMode underwaterAreasMode;
		
		[Resolution(1024, 128, 256, 512, 1024, 2048)]
		[SerializeField]
		private int mapResolution = 1024;

		[Tooltip("All waves bigger than this (in scene units) will be dampened near the shore.")]
		[SerializeField]
		private float waveDampingThreshold = 4.0f;

		private RenderTexture intensityMask;
		private MeshRenderer interactionMaskRenderer;
		private Material interactionMaskMaterial;
		private Bounds bounds;
		private Bounds totalBounds;

		private float[] heightMapData;
		private float offsetX, offsetZ, scaleX, scaleZ;
		private int width, height;

		public static List<StaticWaterInteraction> staticWaterInteractions = new List<StaticWaterInteraction>();

		private void Start()
		{
			OnValidate();

			if (intensityMask == null)
				RenderShorelineIntensityMask();

			if(interactionMaskRenderer == null)
				CreateMaskRenderer();
		}

		public static StaticWaterInteraction AttachTo(GameObject target, float shoreSmoothness, bool hasBottomFaces, UnderwaterAreasMode underwaterAreasMode, int mapResolution, float waveDampingThreshold = 4.0f)
		{
			var instance = target.AddComponent<StaticWaterInteraction>();
			instance.shoreSmoothness = shoreSmoothness;
			instance.hasBottomFaces = hasBottomFaces;
			instance.underwaterAreasMode = underwaterAreasMode;
			instance.mapResolution = mapResolution;
			instance.waveDampingThreshold = waveDampingThreshold;
			return instance;
		}

		public Bounds Bounds
		{
			get { return totalBounds; }
		}

		public RenderTexture IntensityMask
		{
			get
			{
				if(intensityMask == null)
				{
					intensityMask = new RenderTexture(mapResolution, mapResolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
					{
						hideFlags = HideFlags.DontSave,
						filterMode = FilterMode.Bilinear
					};
				}

				return intensityMask;
			}
		}

		public Renderer InteractionRenderer
		{
			get { return interactionMaskRenderer; }
		}

		private void OnValidate()
		{
			if(maskGenerateShader == null)
				maskGenerateShader = Shader.Find("PlayWay Water/Utility/ShorelineMaskGenerate");

			if(maskDisplayShader == null)
				maskDisplayShader = Shader.Find("PlayWay Water/Utility/ShorelineMaskRender");

			if(heightMapperShader == null)
				heightMapperShader = Shader.Find("PlayWay Water/Utility/HeightMapper");

			if(heightMapperShaderAlt == null)
				heightMapperShaderAlt = Shader.Find("PlayWay Water/Utility/HeightMapperAlt");

			if(interactionMaskMaterial != null)
				interactionMaskMaterial.SetFloat("_WaveDampingThreshold", waveDampingThreshold);
		}

		private void OnEnable()
		{
			staticWaterInteractions.Add(this);
			DynamicWater.RegisterInteraction(this);
		}

		private void OnDisable()
		{
			DynamicWater.UnregisterInteraction(this);
			staticWaterInteractions.Remove(this);
		}

		private void OnDestroy()
		{
			if (intensityMask != null)
			{
				intensityMask.Destroy();
				intensityMask = null;
			}

			if (interactionMaskMaterial != null)
			{
				interactionMaskMaterial.Destroy();
				interactionMaskMaterial = null;
			}

			if (interactionMaskRenderer != null)
			{
				interactionMaskRenderer.Destroy();
				interactionMaskRenderer = null;
			}
		}

		/// <summary>
		/// Call it after changes are made to the associated renderers to update internal data.
		/// </summary>
		[ContextMenu("Refresh Intensity Mask (Runtime Only)")]
		public void Refresh()
		{
			if(interactionMaskRenderer == null)
				return;			// it hadn't started, it will refresh anyway

			RenderShorelineIntensityMask();

			Vector3 maskLocalScale = totalBounds.size * 0.5f;
			maskLocalScale.x /= transform.localScale.x;
			maskLocalScale.y /= transform.localScale.y;
			maskLocalScale.z /= transform.localScale.z;
			interactionMaskRenderer.gameObject.transform.localScale = maskLocalScale;
		}

		public void SetUniformDepth(float depth, float boundsSize)
		{
			OnValidate();
			OnDestroy();

			totalBounds = new Bounds(transform.position + new Vector3(boundsSize * 0.5f, 0.0f, boundsSize * 0.5f), new Vector3(boundsSize, 1.0f, boundsSize));

			var intensityMask = IntensityMask;

			float f = Mathf.Sqrt((float)Math.Tanh(depth*-0.01));
			Graphics.SetRenderTarget(intensityMask);
			GL.Clear(true, true, new Color(f, f, f, f));
			Graphics.SetRenderTarget(null);

			if(interactionMaskRenderer == null)
				CreateMaskRenderer();
		}

		private void RenderShorelineIntensityMask()
		{
			try
			{
				PrepareRenderers();

				float shoreSteepness = 1.0f / shoreSmoothness;
				totalBounds = bounds;

				if(underwaterAreasMode == UnderwaterAreasMode.Generate)
				{
					float distanceToFullSeaInMeters = 80.0f / shoreSteepness;
					totalBounds.Expand(new Vector3(distanceToFullSeaInMeters, 0.0f, distanceToFullSeaInMeters));
				}

				float heightOffset = transform.position.y;
				var heightMap = RenderHeightMap(mapResolution, mapResolution);

				var intensityMask = IntensityMask;

				offsetX = -totalBounds.min.x;
				offsetZ = -totalBounds.min.z;
				scaleX = mapResolution / totalBounds.size.x;
				scaleZ = mapResolution / totalBounds.size.z;
				width = mapResolution;
				height = mapResolution;

				var temp1 = RenderTexture.GetTemporary(mapResolution, mapResolution, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
				var temp2 = RenderTexture.GetTemporary(mapResolution, mapResolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);

				var material = new Material(maskGenerateShader);
				material.SetVector("_ShorelineExtendRange", new Vector2(totalBounds.size.x / bounds.size.x - 1.0f, totalBounds.size.z / bounds.size.z - 1.0f));
				material.SetFloat("_TerrainMinPoint", heightOffset);
				material.SetFloat("_Steepness", Mathf.Max(totalBounds.size.x, totalBounds.size.z) * shoreSteepness);
				material.SetFloat("_Offset1", 1.0f / mapResolution);
				material.SetFloat("_Offset2", 1.41421356f / mapResolution);

				RenderTexture distanceMapB = null;

				if(underwaterAreasMode == UnderwaterAreasMode.Generate)
				{
					// distance map
					var distanceMapA = RenderTexture.GetTemporary(mapResolution, mapResolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
					distanceMapB = RenderTexture.GetTemporary(mapResolution, mapResolution, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
					Graphics.Blit(heightMap, distanceMapA, material, 2);
					ComputeDistanceMap(material, distanceMapA, distanceMapB);
					RenderTexture.ReleaseTemporary(distanceMapA);

					distanceMapB.filterMode = FilterMode.Bilinear;

					// create filtered height map
					material.SetTexture("_DistanceMap", distanceMapB);
					material.SetFloat("_GenerateUnderwaterAreas", 1.0f);
				}
				else
					material.SetFloat("_GenerateUnderwaterAreas", 0.0f);

				Graphics.Blit(heightMap, temp1, material, 0);
				RenderTexture.ReleaseTemporary(heightMap);

				if(distanceMapB != null)
					RenderTexture.ReleaseTemporary(distanceMapB);

				Graphics.Blit(temp1, temp2);
				ReadBackHeightMap(temp1);

				// create intensity mask
				Graphics.Blit(temp1, intensityMask, material, 1);

				RenderTexture.ReleaseTemporary(temp2);
				RenderTexture.ReleaseTemporary(temp1);
				Destroy(material);
			}
			finally
			{
				RestoreRenderers();
			}
		}

		private GameObject[] gameObjects;
		private Terrain[] terrains;
		private int[] originalRendererLayers;
		private float[] originalTerrainPixelErrors;

		private void PrepareRenderers()
		{
			bool hasBounds = false;
			bounds = new Bounds();

			var gameObjectsList = new List<GameObject>();
			var renderers = GetComponentsInChildren<Renderer>(false);

			for(int i = 0; i < renderers.Length; ++i)
			{
				if(renderers[i].name == "Shoreline Mask")
					continue;

				var swi = renderers[i].GetComponent<StaticWaterInteraction>();

				if(swi == null || swi == this)
				{
					gameObjectsList.Add(renderers[i].gameObject);

					if (hasBounds)
						bounds.Encapsulate(renderers[i].bounds);
					else
					{
						bounds = renderers[i].bounds;
						hasBounds = true;
					}
				}
			}

			terrains = GetComponentsInChildren<Terrain>(false);
			originalTerrainPixelErrors = new float[terrains.Length];

			for(int i = 0; i < terrains.Length; ++i)
			{
				originalTerrainPixelErrors[i] = terrains[i].heightmapPixelError;

				var swi = terrains[i].GetComponent<StaticWaterInteraction>();

				if(swi == null || swi == this)
				{
					gameObjectsList.Add(terrains[i].gameObject);
					terrains[i].heightmapPixelError = 1.0f;

					if (hasBounds)
					{
						bounds.Encapsulate(terrains[i].transform.position);
						bounds.Encapsulate(terrains[i].transform.position + terrains[i].terrainData.size);
					}
					else
					{
						bounds = new Bounds(terrains[i].transform.position + terrains[i].terrainData.size * 0.5f, terrains[i].terrainData.size);
						hasBounds = true;
					}
				}
			}

			gameObjects = gameObjectsList.ToArray();
			originalRendererLayers = new int[gameObjects.Length];

			for(int i = 0; i < gameObjects.Length; ++i)
			{
				originalRendererLayers[i] = gameObjects[i].layer;
				gameObjects[i].layer = WaterProjectSettings.Instance.WaterTempLayer;
			}
		}

		private void RestoreRenderers()
		{
			if(terrains != null)
			{
				for(int i = 0; i < terrains.Length; ++i)
					terrains[i].heightmapPixelError = originalTerrainPixelErrors[i];
			}

			if(gameObjects != null)
			{
				for(int i = gameObjects.Length - 1; i >= 0; --i)
					gameObjects[i].layer = originalRendererLayers[i];
			}
		}

		private RenderTexture RenderHeightMap(int width, int height)
		{
			var heightMap = RenderTexture.GetTemporary(width, height, 32, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
			heightMap.wrapMode = TextureWrapMode.Clamp;

			RenderTexture.active = heightMap;
			GL.Clear(true, true, new Color(-4000.0f, -4000.0f, -4000.0f, -4000.0f), 1000000.0f);
			RenderTexture.active = null;

			var cameraGo = new GameObject();
			var camera = cameraGo.AddComponent<Camera>();
			camera.enabled = false;
			camera.clearFlags = CameraClearFlags.Nothing;
			camera.depthTextureMode = DepthTextureMode.None;
			camera.orthographic = true;
			camera.cullingMask = 1 << WaterProjectSettings.Instance.WaterTempLayer;
			camera.nearClipPlane = 0.95f;
			camera.farClipPlane = bounds.size.y + 2.0f;
			camera.orthographicSize = bounds.size.z * 0.5f;
			camera.aspect = bounds.size.x / bounds.size.z;

			Vector3 cameraPosition = bounds.center;
			cameraPosition.y = bounds.max.y + 1.0f;

			camera.transform.position = cameraPosition;
			camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

			camera.targetTexture = heightMap;
			camera.RenderWithShader(hasBottomFaces ? heightMapperShaderAlt : heightMapperShader, "RenderType");
			camera.targetTexture = null;

			Destroy(cameraGo);

			return heightMap;
		}

		private static void ComputeDistanceMap(Material material, RenderTexture sa, RenderTexture sb)
		{
			sa.filterMode = FilterMode.Point;
			sb.filterMode = FilterMode.Point;
			
			var a = sa;
			var b = sb;
			int w = (int)(Mathf.Max(sa.width, sa.height) * 0.7f);

			for(int i = 0; i < w; ++i)
			{
				Graphics.Blit(a, b, material, 3);

				var t = a;
				a = b;
				b = t;
			}

			// ensure that result is in b tex
			if(a != sb)
				Graphics.Blit(a, sb, material, 3);
		}

		private void ReadBackHeightMap(RenderTexture source)
		{
			int width = intensityMask.width;
			int height = intensityMask.height;

			heightMapData = new float[width * height + width + 1];

			RenderTexture.active = source;
			var gpuDownloadTex = new Texture2D(intensityMask.width, intensityMask.height, TextureFormat.RGBAFloat, false, true);
			gpuDownloadTex.ReadPixels(new Rect(0, 0, intensityMask.width, intensityMask.height), 0, 0);
			gpuDownloadTex.Apply();
			RenderTexture.active = null;

			int index = 0;

			for(int y = 0; y < height; ++y)
			{
				for(int x = 0; x < width; ++x)
				{
					float h = gpuDownloadTex.GetPixel(x, y).r;

					if(h > 0.0f && h < 1.0f)
						h = Mathf.Sqrt(h);

					heightMapData[index++] = h;
				}
			}

			Destroy(gpuDownloadTex);
		}

		private void CreateMaskRenderer()
		{
			var go = new GameObject("Shoreline Mask")
			{
				hideFlags = HideFlags.DontSave,
				layer = WaterProjectSettings.Instance.WaterTempLayer
			};

			var mf = go.AddComponent<MeshFilter>();
			mf.sharedMesh = PlayWay.Water.Internal.Quads.BipolarXZ;

			interactionMaskMaterial = new Material(maskDisplayShader) {hideFlags = HideFlags.DontSave};
			interactionMaskMaterial.SetTexture("_MainTex", intensityMask);
			interactionMaskMaterial.SetFloat("_WaveDampingThreshold", waveDampingThreshold);

			interactionMaskRenderer = go.AddComponent<MeshRenderer>();
			interactionMaskRenderer.sharedMaterial = interactionMaskMaterial;
			interactionMaskRenderer.enabled = false;

			go.transform.SetParent(transform);
			go.transform.position = new Vector3(totalBounds.center.x, 0.0f, totalBounds.center.z);
			go.transform.localRotation = Quaternion.identity;

			Vector3 maskLocalScale = totalBounds.size * 0.5f;
			maskLocalScale.x /= transform.localScale.x;
			maskLocalScale.y /= transform.localScale.y;
			maskLocalScale.z /= transform.localScale.z;
			go.transform.localScale = maskLocalScale;
		}

		public float GetDepthAt(float x, float z)
		{
			x = (x + offsetX) * scaleX;
			z = (z + offsetZ) * scaleZ;

			int ix = (int)x; if(ix > x) --ix;       // inlined FastMath.FloorToInt(x);
			int iz = (int)z; if(iz > z) --iz;       // inlined FastMath.FloorToInt(z);

			if(ix >= width || ix < 0 || iz >= height || iz < 0)
				return 100.0f;

			x -= ix;
			z -= iz;

			int index = iz * width + ix;

			float a = heightMapData[index] * (1.0f - x) + heightMapData[index + 1] * x;
			float b = heightMapData[index + width] * (1.0f - x) + heightMapData[index + width + 1] * x;

			return a * (1.0f - z) + b * z;
		}

		public static float GetTotalDepthAt(float x, float z)
		{
			float minDepth = 100.0f;
			int numInteractions = staticWaterInteractions.Count;

			for(int i = 0; i < numInteractions; ++i)
			{
				float depth = staticWaterInteractions[i].GetDepthAt(x, z);

				if(minDepth > depth)
					minDepth = depth;
			}

			return minDepth;
		}

		public void OnInteractionPreRender(Camera camera, float waterVerticalOffset, int layerMask)
		{
			if(((1 << gameObject.layer) & layerMask) != 0)
			{
				Vector3 pos = interactionMaskRenderer.transform.position;
				pos.y = waterVerticalOffset;
				interactionMaskRenderer.transform.position = pos;
                interactionMaskRenderer.enabled = true;
			}
		}

		public void OnInteractionPostRender(DynamicWaterCameraData overlays)
		{
			interactionMaskRenderer.enabled = false;
		}

		public void RenderInteractionDirect()
		{
			interactionMaskRenderer.sharedMaterial.SetPass(0);
			Graphics.DrawMeshNow(interactionMaskRenderer.GetComponent<MeshFilter>().sharedMesh, interactionMaskRenderer.transform.localToWorldMatrix, 0);
		}

		public enum UnderwaterAreasMode
		{
			Generate,
			UseExisting
		}
	}
}
