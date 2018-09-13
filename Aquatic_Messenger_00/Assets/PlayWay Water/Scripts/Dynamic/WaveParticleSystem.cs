using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Simulates wave particles on the water surface.
	/// </summary>
	[RequireComponent(typeof(DynamicWater))]
	[AddComponentMenu("Water/Waves Particle System", 1)]
	public sealed class WaveParticleSystem : MonoBehaviour, IOverlaysRenderer
	{
		[HideInInspector]
		[SerializeField]
		private Shader waterWavesParticlesShader;

		[SerializeField]
		private int maxParticles = 50000;

		[SerializeField]
		private int maxParticlesPerTile = 2000;

		[SerializeField]
		private float prewarmTime = 40.0f;

		[Tooltip("Allowed execution time per frame.")]
		[SerializeField]
		private float timePerFrame = 0.8f;

		private WaveParticlesQuadtree particles;

		private Water water;
		private Material waterWavesParticlesMaterial;
		private float simulationTime;
		private float timePerFrameExp;
        private bool prewarmed;

		private readonly List<IWavesParticleSystemPlugin> plugins;

		public WaveParticleSystem()
		{
			plugins = new List<IWavesParticleSystemPlugin>();
		}

		private void Awake()
		{
			water = GetComponent<Water>();
			OnValidate();
		}

		private void OnEnable()
		{
			CheckResources();
		}

		private void OnDisable()
		{
			FreeResources();
		}

		public int ParticleCount
		{
			get { return particles.Count; }
		}
		
		public float SimulationTime
		{
			get { return simulationTime; }
		}

		public bool AddParticle(WaveParticle particle)
		{
			if(particle != null)
			{
				if(particle.group == null)
					throw new System.ArgumentException("Particle has no group");
				
				return particles.AddElement(particle);
			}

			return false;
		}
		
		public bool Spawn(WaveParticle particle, int clones, float waveShapeIrregularity, float centerElevation = 2.0f, float edgesElevation = 0.35f)
		{
			if(particle == null || particles.FreeSpace < clones * 2 + 1)
				return false;

			particle.group = new WaveParticlesGroup(simulationTime);
			particle.baseAmplitude *= water.UniformWaterScale;
			particle.baseFrequency /= water.UniformWaterScale;

			WaveParticle previousParticle = null;

			float minAmplitude = 1.0f / waveShapeIrregularity;

			for(int i=-clones; i<=clones; ++i)
			{
				var p = particle.Clone(particle.position + new Vector2(particle.direction.y, -particle.direction.x) * (i * 1.48f / particle.baseFrequency));

				if(p == null)
					continue;

				p.amplitudeModifiers2 = Random.Range(minAmplitude, 1.0f) * (edgesElevation + (0.5f + Mathf.Cos(Mathf.PI * i / clones) * 0.5f) * (centerElevation - edgesElevation));
				p.leftNeighbour = previousParticle;

				if(previousParticle != null)
				{
					previousParticle.rightNeighbour = p;

					if(i == clones)
						p.disallowSubdivision = true;           // it's a last particle of the group
				}
				else
				{
					p.group.leftParticle = p;               // it's a first particle of the group
					p.disallowSubdivision = true;
				}
				
				if(!particles.AddElement(p))
					return previousParticle != null;

				previousParticle = p;
			}

			return true;
		}

		public void RenderOverlays(DynamicWaterCameraData overlays)
		{
			
		}

		public void RenderFoam(DynamicWaterCameraData overlays)
		{
			if(enabled)
				RenderParticles(overlays);
		}

		private void OnValidate()
		{
			timePerFrameExp = Mathf.Exp(timePerFrame * 0.5f);

			if(waterWavesParticlesShader == null)
				waterWavesParticlesShader = Shader.Find("PlayWay Water/Particles/Particles");

			if(particles != null)
				particles.DebugMode = water.ShaderSet.LocalEffectsDebug;
        }

		private void LateUpdate()
		{
			if(!prewarmed)
				Prewarm();
			
			UpdateSimulation(Time.deltaTime);
		}

		public void RegisterPlugin(IWavesParticleSystemPlugin plugin)
		{
			if(!plugins.Contains(plugin))
				plugins.Add(plugin);
		}

		public void UnregisterPlugin(IWavesParticleSystemPlugin plugin)
		{
			plugins.Remove(plugin);
		}

		private void Prewarm()
		{
			prewarmed = true;

			while(simulationTime < prewarmTime)
				UpdateSimulationWithoutFrameBudget(0.1f);
		}

		private void UpdateSimulation(float deltaTime)
		{
			simulationTime += deltaTime;

			UpdatePlugins(deltaTime);
			particles.UpdateSimulation(simulationTime, timePerFrameExp);
		}

		private void UpdateSimulationWithoutFrameBudget(float deltaTime)
		{
			simulationTime += deltaTime;

			UpdatePlugins(deltaTime);
			particles.UpdateSimulation(simulationTime);
		}

		private void UpdatePlugins(float deltaTime)
		{
			int numPlugins = plugins.Count;
			for(int i = 0; i < numPlugins; ++i)
				plugins[i].UpdateParticles(simulationTime, deltaTime);
		}

		private void RenderParticles(DynamicWaterCameraData overlays)
		{
			var spray = GetComponent<Spray>();

			if(spray != null && spray.ParticlesBuffer != null)
				Graphics.SetRandomWriteTarget(3, spray.ParticlesBuffer);

			if(!water.ShaderSet.LocalEffectsDebug)
				Graphics.SetRenderTarget(new[] { overlays.DynamicDisplacementMap.colorBuffer, overlays.NormalMap.colorBuffer }, overlays.DynamicDisplacementMap.depthBuffer);
			else
				Graphics.SetRenderTarget(new[] { overlays.DynamicDisplacementMap.colorBuffer, overlays.NormalMap.colorBuffer, overlays.GetDebugMap(true).colorBuffer }, overlays.DynamicDisplacementMap.depthBuffer);
			
			Shader.SetGlobalMatrix("_ParticlesVP", GL.GetGPUProjectionMatrix(overlays.Camera.PlaneProjectorCamera.projectionMatrix, true) * overlays.Camera.PlaneProjectorCamera.worldToCameraMatrix);

			Vector4 localMapsShaderCoords = overlays.Camera.LocalMapsShaderCoords;
			float uniformWaterScale = GetComponent<Water>().UniformWaterScale;
            waterWavesParticlesMaterial.SetFloat("_WaterScale", uniformWaterScale);
			waterWavesParticlesMaterial.SetVector("_LocalMapsCoords", localMapsShaderCoords);
			waterWavesParticlesMaterial.SetPass(water.ShaderSet.LocalEffectsDebug ? 1 : 0);

			particles.Render(overlays.Camera.LocalMapsRect);

			Graphics.ClearRandomWriteTargets();
		}
		
		private void CheckResources()
		{
			if(waterWavesParticlesMaterial == null)
				waterWavesParticlesMaterial = new Material(waterWavesParticlesShader) {hideFlags = HideFlags.DontSave};

			if(particles == null)
			{
				particles = new WaveParticlesQuadtree(new Rect(-1000.0f, -1000.0f, 2000.0f, 2000.0f), maxParticlesPerTile,
					maxParticles) {DebugMode = water.ShaderSet.LocalEffectsDebug };
			}
		}

		private void FreeResources()
		{
			if(waterWavesParticlesMaterial != null)
			{
				waterWavesParticlesMaterial.Destroy();
				waterWavesParticlesMaterial = null;
			}
		}
	}
}
