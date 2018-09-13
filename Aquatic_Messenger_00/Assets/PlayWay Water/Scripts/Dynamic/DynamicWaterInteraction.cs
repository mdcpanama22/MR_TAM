using System.Collections.Generic;
using PlayWay.Water.Internal;
using UnityEngine;

namespace PlayWay.Water
{
	public sealed class DynamicWaterInteraction : MonoBehaviour, IWaterInteraction
	{
		[HideInInspector] [SerializeField] private ComputeShader colliderInteractionShader;
		[HideInInspector] [SerializeField] private Shader maskDisplayShader;

		[SerializeField] private WaveParticlesSystemGPU water;
		[SerializeField] private bool foamTrail;
		[SerializeField] private bool waveInteractions;
		[SerializeField] private bool staticOrientationOptimization = true;
		[SerializeField] private Blur blur;

		private Material[] materials;
		private string[] tags;
		private GameObject[] gameObjects;
		private int[] gameObjectLayers;
		private Bounds totalBounds;
		private ComputeBuffer colliderVerticesBuffer;
		private ComputeBuffer objectToWorld;

		// prerendered mask
		private RenderTexture prerenderedMask;
		private MeshRenderer interactionMaskRenderer;

		private static Matrix4x4[] matrixTemp = new Matrix4x4[1];

		private void Start()
		{
			/*var materials = new List<Material>();
			var renderers = GetComponentsInChildren<Renderer>();

			gameObjects = new GameObject[renderers.Length];
			gameObjectLayers = new int[renderers.Length];

			totalBounds = renderers.Length != 0 ? renderers[0].bounds : new Bounds();

			for(int i=0; i<renderers.Length; ++i)
			{
				gameObjects[i] = renderers[i].gameObject;
				gameObjectLayers[i] = gameObjects[i].layer;

				totalBounds.Encapsulate(renderers[i].bounds);

				var sharedMaterials = renderers[i].sharedMaterials;

				for(int ii = 0; ii < sharedMaterials.Length; ++ii)
				{
					if(!materials.Contains(sharedMaterials[ii]) && sharedMaterials[ii] != null)
						materials.Add(sharedMaterials[ii]);
				}
			}

			this.materials = materials.ToArray();
			tags = new string[this.materials.Length];

			for(int i = 0; i < this.materials.Length; ++i)
				tags[i] = this.materials[i].GetTag("RenderType", true, "");

			for(int i = 0; i < this.materials.Length; ++i)
				this.materials[i].SetOverrideTag("CustomType", "WaterInteractionDynamic");

			if(staticOrientationOptimization)
			{
				PrerenderMask();
				CreateMaskRenderer();
            }*/

			CreateComputeBuffers();
		}

		private void OnEnable()
		{
			DynamicWater.RegisterInteraction(this);
		}

		private void OnDisable()
		{
			DynamicWater.UnregisterInteraction(this);
		}

		private void OnDestroy()
		{
			if (colliderVerticesBuffer != null)
			{
				colliderVerticesBuffer.Release();
				colliderVerticesBuffer = null;
			}

			if (objectToWorld != null)
			{
				objectToWorld.Release();
				objectToWorld = null;
			}
		}

		private void OnValidate()
		{
			if(maskDisplayShader == null)
				maskDisplayShader = Shader.Find("PlayWay Water/Utility/ShorelineMaskRender");

#if UNITY_EDITOR
			if(colliderInteractionShader == null)
			{
				var guids = UnityEditor.AssetDatabase.FindAssets("\"ColliderInteraction\" t:ComputeShader");

				if(guids.Length != 0)
				{
					string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
					colliderInteractionShader = (ComputeShader)UnityEditor.AssetDatabase.LoadAssetAtPath(path, typeof(ComputeShader));
					UnityEditor.EditorUtility.SetDirty(this);
				}
			}
#endif

			blur.Validate();
		}

		public void OnInteractionPreRender(Camera camera, float waterVerticalOffset, int layerMask)
		{
			/*if(staticOrientationOptimization)
			{
				interactionMaskRenderer.enabled = true;
				Vector3 pos = interactionMaskRenderer.transform.position;
				pos.y = waterVerticalOffset;
				interactionMaskRenderer.transform.position = pos;
            }
			else
			{
				int waterTempLayer = WaterProjectSettings.Instance.WaterTempLayer;

				for(int i = 0; i < gameObjects.Length; ++i)
					gameObjects[i].layer = waterTempLayer;
			}*/
		}

		public void OnInteractionPostRender(DynamicWaterCameraData overlays)
		{
			/*if(staticOrientationOptimization)
			{
				interactionMaskRenderer.enabled = false;
			}
			else
			{
				for(int i = 0; i < gameObjects.Length; ++i)
					gameObjects[i].layer = gameObjectLayers[i];
			}*/

			matrixTemp[0] = transform.localToWorldMatrix;
			objectToWorld.SetData(matrixTemp);

			colliderInteractionShader.SetVector("_LocalMapsCoords", overlays.Camera.LocalMapsShaderCoords);
			colliderInteractionShader.SetTexture(0, "TotalDisplacementMap", overlays.GetTotalDisplacementMap());
			colliderInteractionShader.SetBuffer(0, "Vertices", colliderVerticesBuffer);
			colliderInteractionShader.SetBuffer(0, "Particles", water.ParticlesBuffer);
			colliderInteractionShader.SetBuffer(0, "ObjectToWorld", objectToWorld);
			colliderInteractionShader.Dispatch(0, Mathf.CeilToInt((colliderVerticesBuffer.count >> 1) / 256.0f), 1, 1);
		}

		public void RenderInteractionDirect()
		{
			
		}

		private void PrerenderMask()
		{
			prerenderedMask = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
			{
				hideFlags = HideFlags.DontSave,
				name = "Prerendered Water Interaction Mask"
			};

			var cameraGo = new GameObject();

			try
			{
				var camera = cameraGo.AddComponent<Camera>();
				camera.enabled = false;
				camera.depthTextureMode = DepthTextureMode.None;
				camera.renderingPath = RenderingPath.VertexLit;
				camera.orthographic = true;
				camera.orthographicSize = totalBounds.size.magnitude * 0.5f * (1.0f + blur.TotalSize * 2.0f);
				camera.cullingMask = 1 << WaterProjectSettings.Instance.WaterTempLayer;
				camera.farClipPlane = 2000.0f;
				camera.clearFlags = CameraClearFlags.SolidColor;
				camera.backgroundColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
				camera.transform.position = new Vector3(totalBounds.center.x, 1000.0f, totalBounds.center.z);
				camera.transform.rotation = Quaternion.LookRotation(new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f));
				camera.targetTexture = prerenderedMask;

				int waterTempLayer = WaterProjectSettings.Instance.WaterTempLayer;

				for(int i = 0; i < gameObjects.Length; ++i)
					gameObjects[i].layer = waterTempLayer;

				camera.RenderWithShader(maskDisplayShader, "CustomType");
				camera.targetTexture = null;

				for(int i = 0; i < gameObjects.Length; ++i)
					gameObjects[i].layer = gameObjectLayers[i];
			}
			finally
			{
				Destroy(cameraGo);
			}

			blur.Apply(prerenderedMask);
		}

		private void CreateMaskRenderer()
		{
			var go = new GameObject("Water Interaction Mask")
			{
				hideFlags = HideFlags.DontSave,
				layer = WaterProjectSettings.Instance.WaterTempLayer
			};

			var mf = go.AddComponent<MeshFilter>();
			mf.sharedMesh = PlayWay.Water.Internal.Quads.BipolarXZ;

			var material = new Material(maskDisplayShader) {hideFlags = HideFlags.DontSave};
			material.SetTexture("_MainTex", prerenderedMask);

			interactionMaskRenderer = go.AddComponent<MeshRenderer>();
			interactionMaskRenderer.sharedMaterial = material;
			interactionMaskRenderer.enabled = false;

			go.transform.SetParent(transform);
			go.transform.position = new Vector3(totalBounds.center.x, 0.0f, totalBounds.center.z);
			go.transform.localRotation = Quaternion.identity;
			
			float t = totalBounds.size.magnitude * 0.5f * (1.0f + blur.TotalSize * 2.0f);
            Vector3 maskLocalScale;
			maskLocalScale.x = t / transform.localScale.x;
			maskLocalScale.y = t / transform.localScale.y;
			maskLocalScale.z = t / transform.localScale.z;
			go.transform.localScale = maskLocalScale;
		}

		private void CreateComputeBuffers()
		{
			var collider = GetComponent<MeshCollider>();
			var colliderMesh = collider.sharedMesh;
			var vertices = colliderMesh.vertices;
			var normals = colliderMesh.normals;
			var indices = colliderMesh.GetIndices(0);
			
			colliderVerticesBuffer = new ComputeBuffer(indices.Length * 2, 24, ComputeBufferType.Default);
			objectToWorld = new ComputeBuffer(1, 64, ComputeBufferType.Default);

			var colliderVerticesRaw = new VertexData[indices.Length * 2];
			int index = 0;

			for (int i = 0; i < indices.Length; ++i)
			{
				int currentIndex = indices[i];
				int previousIndex = indices[i%3 == 0 ? i + 2 : i - 1];

				colliderVerticesRaw[index++] = new VertexData()
				{
					position = vertices[previousIndex],
					normal = normals[previousIndex]
				};

				colliderVerticesRaw[index++] = new VertexData()
				{
					position = vertices[currentIndex],
					normal = normals[currentIndex]
				};
			}

			colliderVerticesBuffer.SetData(colliderVerticesRaw);
		}

		private struct VertexData
		{
			public Vector3 position;
			public Vector3 normal;
		}
	}
}
