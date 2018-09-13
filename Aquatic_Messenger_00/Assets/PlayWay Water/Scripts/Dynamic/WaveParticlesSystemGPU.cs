using UnityEngine;

namespace PlayWay.Water
{
	public class WaveParticlesSystemGPU : MonoBehaviour, IOverlaysRenderer
	{
		[SerializeField] private int maxParticles = 80000;
		[SerializeField] private ComputeShader controllerShader;
		[SerializeField] private Shader particlesRenderShader;
		[SerializeField] private Texture foamTexture;
		[SerializeField] private Texture foamOverlayTexture;
		[SerializeField] private int foamAtlasWidth = 8;
		[SerializeField] private int foamAtlasHeight = 4;

		private Material particlesRenderMaterial;
		private ComputeBuffer particlesA;
		private ComputeBuffer particlesB;
		private ComputeBuffer spawnBuffer;
		private ComputeBuffer particlesRenderInfo;
		private ComputeBuffer particlesUpdateInfo;
		private Vector2 lastSurfaceOffset;
		private uint particlesToSpawnCount;
		private Water water;

		private readonly int[] countBuffer = new int[4];
		private readonly ParticleData[] particlesToSpawn = new ParticleData[16];
		private readonly RenderBuffer[] renderBuffers = new RenderBuffer[2];

		private static int foamAtlasParamsId, foamAtlasId, foamOverlayTextureId;

		private void Start()
		{
			water = GetComponentInParent<Water>();

			OnValidate();

			particlesRenderMaterial = new Material(particlesRenderShader) {hideFlags = HideFlags.DontSave};
			SetMaterialProperties();

			lastSurfaceOffset = water.SurfaceOffset;
		}

		private void OnDestroy()
		{
			if (particlesA != null)
			{
				particlesA.Release();
				particlesA = null;
			}

			if (particlesB != null)
			{
				particlesB.Release();
				particlesB = null;
			}

			if (particlesRenderInfo != null)
			{
				particlesRenderInfo.Release();
				particlesRenderInfo = null;
			}

			if (particlesUpdateInfo != null)
			{
				particlesUpdateInfo.Release();
				particlesUpdateInfo = null;
			}

			if(spawnBuffer != null)
			{
				spawnBuffer.Release();
				spawnBuffer = null;
			}
		}

		public ComputeBuffer ParticlesBuffer
		{
			get { return particlesA; }
		}

		public int FoamAtlasWidth
		{
			get { return foamAtlasWidth; }
		}

		public int FoamAtlasHeight
		{
			get { return foamAtlasHeight; }
		}

		public void EmitParticle(ParticleData particleData)
		{
			if(particlesToSpawnCount == particlesToSpawn.Length)
				return;
			
			particlesToSpawn[particlesToSpawnCount++] = particleData;
		}

		public void RenderOverlays(DynamicWaterCameraData overlays)
		{
			var spray = GetComponent<Spray>();

			if(spray != null && spray.ParticlesBuffer != null)
				Graphics.SetRandomWriteTarget(3, spray.ParticlesBuffer);

			renderBuffers[0] = overlays.DynamicDisplacementMap.colorBuffer;
			renderBuffers[1] = overlays.NormalMap.colorBuffer;
			
			particlesRenderMaterial.SetBuffer("_Particles", particlesA);
			particlesRenderMaterial.SetMatrix("_ParticlesVP", GL.GetGPUProjectionMatrix(overlays.Camera.PlaneProjectorCamera.projectionMatrix, true) * overlays.Camera.PlaneProjectorCamera.worldToCameraMatrix);

			// displacement and normals
			particlesRenderMaterial.SetPass(0);
			Graphics.SetRenderTarget(renderBuffers, overlays.DynamicDisplacementMap.depthBuffer);
			Graphics.DrawProceduralIndirect(MeshTopology.Points, particlesRenderInfo);
			Graphics.ClearRandomWriteTargets();
			
			// trails
			particlesRenderMaterial.SetPass(2);
			Graphics.SetRenderTarget(overlays.DisplacementsMask);
			Graphics.DrawProceduralIndirect(MeshTopology.Points, particlesRenderInfo);

			Graphics.SetRenderTarget(null);
		}

		public void RenderFoam(DynamicWaterCameraData overlays)
		{
			// foam
			particlesRenderMaterial.SetPass(1);
			Graphics.SetRenderTarget(overlays.FoamMap);
			Graphics.DrawProceduralIndirect(MeshTopology.Points, particlesRenderInfo);

			// foam trails
			particlesRenderMaterial.SetPass(3);
			Graphics.DrawProceduralIndirect(MeshTopology.Points, particlesRenderInfo);
		}

		private void Update()
		{
			CheckResources();
			UpdateParticles();
			SpawnParticles();
			SwapBuffers();

			ComputeBuffer.CopyCount(particlesA, particlesRenderInfo, 0);
		}

		private void UpdateParticles()
		{
			particlesB.SetCounterValue(0);

			Vector2 surfaceOffset = water.SurfaceOffset;
			ComputeBuffer.CopyCount(particlesA, particlesUpdateInfo, 0);

			controllerShader.SetFloat("deltaTime", Time.deltaTime);
			controllerShader.SetVector("surfaceOffsetDelta", new Vector4(lastSurfaceOffset.x - surfaceOffset.x, lastSurfaceOffset.y - surfaceOffset.y, 0.0f, 0.0f));
			controllerShader.SetBuffer(0, "Particles", particlesB);
			controllerShader.SetBuffer(0, "SourceParticles", particlesA);
			controllerShader.DispatchIndirect(0, particlesUpdateInfo);

			lastSurfaceOffset = surfaceOffset;
		}

		private void SpawnParticles()
		{
			if(particlesToSpawnCount == 0)
				return;
			
			spawnBuffer.SetData(particlesToSpawn);
			
			controllerShader.SetBuffer(1, "Particles", particlesB);
			controllerShader.SetBuffer(1, "SpawnedParticles", spawnBuffer);
			controllerShader.Dispatch(1, 1, 1, 1);

			for (int i = 0; i < particlesToSpawnCount; ++i)
				particlesToSpawn[i].lifetime = 0.0f;

			particlesToSpawnCount = 0;
		}

		private void SwapBuffers()
		{
			var t = particlesA;
			particlesA = particlesB;
			particlesB = t;
		}

		private void CheckResources()
		{
			if (particlesA == null)
			{
				particlesA = new ComputeBuffer(maxParticles, 48, ComputeBufferType.Append);
				particlesA.SetCounterValue(0);

				particlesB = new ComputeBuffer(maxParticles, 48, ComputeBufferType.Append);
				particlesB.SetCounterValue(0);

				spawnBuffer = new ComputeBuffer(16, 48, ComputeBufferType.Default);
			}

			if(particlesRenderInfo == null)
			{
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3
				particlesRenderInfo = new ComputeBuffer(1, 16, ComputeBufferType.DrawIndirect);
#else
				particlesRenderInfo = new ComputeBuffer(1, 16, ComputeBufferType.IndirectArguments);
#endif
				particlesRenderInfo.SetData(new[] { 0, 1, 0, 0 });
			}

			if (particlesUpdateInfo == null)
			{
#if UNITY_5_0 || UNITY_5_1 || UNITY_5_2 || UNITY_5_3
				particlesUpdateInfo = new ComputeBuffer(1, 12, ComputeBufferType.DrawIndirect);
#else
				particlesUpdateInfo = new ComputeBuffer(1, 12, ComputeBufferType.IndirectArguments);
#endif
				particlesUpdateInfo.SetData(new[] { 0, 1, 1 });
			}
		}

		private void OnValidate()
		{
			if (particlesRenderShader == null)
				particlesRenderShader = Shader.Find("PlayWay Water/Particles/GPU_Render");

			if (particlesRenderMaterial != null)
				SetMaterialProperties();

#if UNITY_EDITOR
			if(controllerShader == null)
			{
				var guids = UnityEditor.AssetDatabase.FindAssets("\"WaveParticlesGPU\" t:ComputeShader");

				if(guids.Length != 0)
				{
					string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
					controllerShader = (ComputeShader)UnityEditor.AssetDatabase.LoadAssetAtPath(path, typeof(ComputeShader));
					UnityEditor.EditorUtility.SetDirty(this);
				}
			}
#endif
		}

		private void SetMaterialProperties()
		{
			particlesRenderMaterial.SetVector("_FoamAtlasParams", new Vector4(1.0f / foamAtlasWidth, 1.0f / foamAtlasHeight, 0.0f, 0.0f));
			particlesRenderMaterial.SetTexture("_FoamAtlas", foamTexture);
			particlesRenderMaterial.SetTexture("_FoamOverlayTexture", foamOverlayTexture);
		}

		public struct ParticleData
		{
			public Vector2 position;
			public Vector2 direction;
			public float wavelength;
			public float amplitude;
			public float initialLifetime;
			public float lifetime;
			public float uvOffsetPack;
			public float foam;
			public float trailCalming;
			public float trailFoam;
		}
	}
}
