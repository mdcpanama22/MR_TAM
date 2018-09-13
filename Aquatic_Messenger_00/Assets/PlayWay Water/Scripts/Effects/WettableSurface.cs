using UnityEngine;
using System.Linq;

namespace PlayWay.Water
{
	public sealed class WettableSurface : MonoBehaviour
	{
		[HideInInspector]
		[SerializeField]
		private Shader wettableUtilShader;

		[HideInInspector]
		[SerializeField]
		private Shader wettableUtilNearShader;

		[SerializeField]
		private Water water;

		[Tooltip("Surface wetting near this camera will be more precise.")]
		[SerializeField]
		private WaterCamera mainCamera;

		[Tooltip("Texture space is good for small objects, especially convex ones.\nNear camera mode is better for terrains and big meshes that are static and don't have geometry at the bottom.")]
		[SerializeField]
		private Mode mode;

		[SerializeField]
		private int resolution = 512;

		[Header("Direct references (Optional)")]
		[SerializeField]
		private MeshRenderer[] meshRenderers;

		[SerializeField]
		private Terrain[] terrains;

		private MeshFilter[] meshFilters;
		private Material wettableUtilMaterial;
		private RenderTexture wetnessMapA;
		private RenderTexture wetnessMapB;
		private Camera wettingCamera;
		private Material[] materials;
		private bool[] terrainDrawTrees;
		private int[] terrainLayers;

		private void Awake()
		{
			if (water == null || water.DynamicWater == null)
			{
				enabled = false;
				return;
			}

			OnValidate();

			terrainLayers = new int[terrains.Length];
			terrainDrawTrees = new bool[terrains.Length];
			meshFilters = new MeshFilter[meshRenderers.Length];

			for(int i = 0; i < meshFilters.Length; ++i)
				meshFilters[i] = meshRenderers[i].GetComponent<MeshFilter>();

			wetnessMapA = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
			{
				name = "PlayWay Water: Wetness Map",
				hideFlags = HideFlags.DontSave,
				filterMode = FilterMode.Bilinear
			};

			if(mode == Mode.NearCamera)
			{
				wetnessMapB = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
				{
					name = "PlayWay Water: Wetness Map",
					hideFlags = HideFlags.DontSave,
					filterMode = FilterMode.Bilinear
				};
			}

			var originalMaterials = meshRenderers
				.SelectMany(mr => mr.sharedMaterials)
				.Concat(terrains.Select(t => t.materialTemplate))
				.Distinct()
				.ToArray();

			materials = InstantiateMaterials(originalMaterials);

			OnValidate();

			wettableUtilMaterial = new Material(mode == Mode.TextureSpace ? wettableUtilShader : wettableUtilNearShader)
			{
				hideFlags = HideFlags.DontSave
			};
		}

		public WaterCamera MainCamera
		{
			get { return mainCamera; }
			set { mainCamera = value; }
		}

		private void OnValidate()
		{
			if(wettableUtilShader == null)
				wettableUtilShader = Shader.Find("PlayWay Water/Utility/Wetness Update");

			if(wettableUtilNearShader == null)
				wettableUtilNearShader = Shader.Find("PlayWay Water/Utility/Wetness Update (Near Camera)");

			if(meshRenderers == null || meshRenderers.Length == 0)
				meshRenderers = GetComponentsInChildren<MeshRenderer>(true);

			if(terrains == null || terrains.Length == 0)
				terrains = GetComponentsInChildren<Terrain>(true);

			var materials = this.materials;

			if(!Application.isPlaying)
			{
				if(materials == null)
				{
					materials = meshRenderers
						.SelectMany(mr => mr.sharedMaterials)
						.Concat(terrains.Select(t => t.materialTemplate))
						.ToArray();
				}

				if(mode == Mode.NearCamera)
				{
					for(int i = 0; i < materials.Length; ++i)
						materials[i].EnableKeyword("_WET_NEAR_CAMERA");
				}
				else
				{
					for(int i = 0; i < materials.Length; ++i)
						materials[i].DisableKeyword("_WET_NEAR_CAMERA");
				}
			}
		}

		private void LateUpdate()
		{
			if(!mainCamera.enabled)
				return;
			
			switch(mode)
			{
				case Mode.TextureSpace:
				{
					Graphics.SetRenderTarget(wetnessMapA);
					wettableUtilMaterial.SetPass(0);
					wettableUtilMaterial.SetTexture("_TotalDisplacementMap", water.DynamicWater.GetCameraOverlaysData(mainCamera.GetComponent<Camera>()).GetTotalDisplacementMap());
					wettableUtilMaterial.SetVector("_LocalMapsCoords", mainCamera.LocalMapsShaderCoords);

					for(int i = 0; i < meshFilters.Length; ++i)
						Graphics.DrawMeshNow(meshFilters[i].sharedMesh, meshFilters[i].transform.localToWorldMatrix);

					if(terrains.Length != 0)
					{
						int waterTempLayer = WaterProjectSettings.Instance.WaterTempLayer;

						for(int i = 0; i < terrains.Length; ++i)
						{
							var go = terrains[i].gameObject;
							terrainLayers[i] = go.layer;
							go.layer = waterTempLayer;
						}

						Shader.SetGlobalTexture("_TotalDisplacementMap", water.DynamicWater.GetCameraOverlaysData(mainCamera.GetComponent<Camera>()).GetTotalDisplacementMap());
						Shader.SetGlobalVector("_LocalMapsCoords", mainCamera.LocalMapsShaderCoords);

						var wettingCamera = GetWettingCamera();
						wettingCamera.transform.position = terrains[0].transform.position + terrains[0].terrainData.size * 0.5f;
						wettingCamera.RenderWithShader(wettableUtilShader, "CustomType");

						for(int i = 0; i < terrains.Length; ++i)
							terrains[i].gameObject.layer = terrainLayers[i];
					}

					break;
				}

				case Mode.NearCamera:
				{
					int waterTempLayer = WaterProjectSettings.Instance.WaterTempLayer;

					for(int i = 0; i < terrains.Length; ++i)
					{
						terrainDrawTrees[i] = terrains[i].drawTreesAndFoliage;
						terrains[i].drawTreesAndFoliage = false;

						var go = terrains[i].gameObject;
						terrainLayers[i] = go.layer;
						go.layer = waterTempLayer;
					}

					Shader.SetGlobalTexture("_WetnessMapPrevious", wetnessMapB);
					Shader.SetGlobalTexture("_TotalDisplacementMap", water.DynamicWater.GetCameraOverlaysData(mainCamera.GetComponent<Camera>()).GetTotalDisplacementMap());
					Shader.SetGlobalVector("_LocalMapsCoords", mainCamera.LocalMapsShaderCoords);

					var localMapsRectPrevious = mainCamera.LocalMapsRectPrevious;
					Shader.SetGlobalVector("_LocalMapsCoordsPrevious", new Vector4(-localMapsRectPrevious.xMin / localMapsRectPrevious.width, -localMapsRectPrevious.yMin / localMapsRectPrevious.width, 1.0f / localMapsRectPrevious.width, localMapsRectPrevious.width));

					Rect localMapsRect = mainCamera.LocalMapsRect;
					var wettingCamera = GetWettingCamera();
                    wettingCamera.transform.position = new Vector3(localMapsRect.center.x, terrains[0].transform.position.y + terrains[0].terrainData.size.y + 1.0f, localMapsRect.center.y);
					wettingCamera.orthographicSize = localMapsRect.width * 0.5f;
					wettingCamera.nearClipPlane = 1.0f;
					wettingCamera.farClipPlane = terrains[0].terrainData.size.y * 2.0f;
					wettingCamera.targetTexture = wetnessMapA;
					wettingCamera.clearFlags = CameraClearFlags.Color;
					wettingCamera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
					wettingCamera.RenderWithShader(wettableUtilNearShader, "CustomType");

					for(int i = 0; i < materials.Length; ++i)
						materials[i].SetTexture("_WetnessMap", wetnessMapA);

					for(int i = 0; i < terrains.Length; ++i)
					{
						terrains[i].drawTreesAndFoliage = terrainDrawTrees[i];
						terrains[i].gameObject.layer = terrainLayers[i];
					}

					var t = wetnessMapB;
					wetnessMapB = wetnessMapA;
					wetnessMapA = t;

					for(int i = 0; i < materials.Length; ++i)
						materials[i].SetVector("_LocalMapsCoords", mainCamera.LocalMapsShaderCoords);

					break;
				}
			}
		}

		private Material[] InstantiateMaterials(Material[] materials)
		{
			var instantiatedMaterials = new Material[materials.Length];

			for(int i = 0; i < instantiatedMaterials.Length; ++i)
				instantiatedMaterials[i] = Instantiate(materials[i]);

			for(int i = 0; i < meshRenderers.Length; ++i)
			{
				var mr = meshRenderers[i];
				var sharedMaterials = mr.sharedMaterials;

				for(int ii = 0; ii < sharedMaterials.Length; ++ii)
				{
					int index = System.Array.IndexOf(materials, sharedMaterials[ii]);
					sharedMaterials[ii] = instantiatedMaterials[index];
				}

				mr.sharedMaterials = sharedMaterials;
			}

			for(int i = 0; i < terrains.Length; ++i)
			{
				var terrain = terrains[i];
				int index = System.Array.IndexOf(materials, terrain.materialTemplate);
				terrain.materialTemplate = instantiatedMaterials[index];
			}

			for(int i = 0; i < instantiatedMaterials.Length; ++i)
				instantiatedMaterials[i].SetTexture("_WetnessMap", wetnessMapA);

			return instantiatedMaterials;
		}

		private Camera GetWettingCamera()
		{
			if(wettingCamera == null)
			{
				var wettingCameraGo = new GameObject("Wetting Camera") {hideFlags = HideFlags.DontSave};
				wettingCameraGo.transform.position = new Vector3(0, 100000, 0);
				wettingCameraGo.transform.eulerAngles = new Vector3(90.0f, 0.0f, 0.0f);

				wettingCamera = wettingCameraGo.AddComponent<Camera>();
				wettingCamera.enabled = false;
				wettingCamera.orthographic = true;
				wettingCamera.orthographicSize = 1000000;
				wettingCamera.nearClipPlane = 10;
				wettingCamera.farClipPlane = 1000000;
				wettingCamera.cullingMask = 1 << WaterProjectSettings.Instance.WaterTempLayer;
				wettingCamera.renderingPath = RenderingPath.VertexLit;
				wettingCamera.clearFlags = CameraClearFlags.Nothing;
				wettingCamera.targetTexture = wetnessMapA;
            }

			return wettingCamera;
		}

		public enum Mode
		{
			TextureSpace,
			NearCamera
		}
	}
}
