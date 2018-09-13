using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Displays water spectrum using a few Gerstner waves directly in vertex shader. Works on all platforms.
	/// </summary>
	public sealed class WavesRendererGerstner
	{
		[System.Serializable]
		public class Data
		{
			[Range(0, 20)]
			public int NumGerstners = 20;
		}
		
		private readonly Water water;
		private readonly WindWaves windWaves;
		private readonly Data data;

		private Gerstner4[] gerstnerFours;
		private int lastUpdateFrame;
		private bool enabled;

		public WavesRendererGerstner(Water water, WindWaves windWaves, Data data)
		{
			this.water = water;
			this.windWaves = windWaves;
			this.data = data;
		}

		internal void Enable()
		{
			if(enabled) return;

			enabled = true;
			
			if(Application.isPlaying)
			{
				water.ProfilesManager.Changed.AddListener(OnProfilesChanged);
				FindMostMeaningfulWaves();
			}
		}

		internal void Disable()
		{
			if(!enabled) return;

			enabled = false;
		}

		internal void OnValidate(WindWaves windWaves)
		{
			if(enabled)
				FindMostMeaningfulWaves();
		}

		public bool Enabled
		{
			get { return enabled; }
		}

		private void FindMostMeaningfulWaves()
		{
			windWaves.SpectrumResolver.SetDirectWaveEvaluationMode(data.NumGerstners);
			var directWaves = windWaves.SpectrumResolver.DirectWaves;

			int index = 0;
			int numFours = (directWaves.Length >> 2);
			gerstnerFours = new Gerstner4[numFours];

			// compute texture offsets from the FFT shader to match Gerstner waves to FFT
			var offsets = new Vector2[4];

			for(int i = 0; i < 4; ++i)
			{
				float tileSize = windWaves.TileSizes[i];

				offsets[i].x = tileSize + (0.5f / windWaves.FinalResolution) * tileSize;
				offsets[i].y = -tileSize + (0.5f / windWaves.FinalResolution) * tileSize;
			}

			for(int i = 0; i < numFours; ++i)
			{
				var wave0 = index < directWaves.Length ? new GerstnerWave(directWaves[index++], offsets) : new GerstnerWave();
				var wave1 = index < directWaves.Length ? new GerstnerWave(directWaves[index++], offsets) : new GerstnerWave();
				var wave2 = index < directWaves.Length ? new GerstnerWave(directWaves[index++], offsets) : new GerstnerWave();
				var wave3 = index < directWaves.Length ? new GerstnerWave(directWaves[index++], offsets) : new GerstnerWave();

				gerstnerFours[i] = new Gerstner4(wave0, wave1, wave2, wave3);
			}
			
			UpdateMaterial();
		}

		private void UpdateMaterial()
		{
			var block = water.Renderer.PropertyBlock;
			//block.SetVector("_GerstnerOrigin", new Vector4(water.TileSize + (0.5f / water.SpectraRenderer.FinalResolution) * water.TileSize, -water.TileSize + (0.5f / water.SpectraRenderer.FinalResolution) * water.TileSize, 0.0f, 0.0f));

			for(int index = 0; index < gerstnerFours.Length; ++index)
			{
				var gerstner4 = gerstnerFours[index];

				Vector4 amplitude, directionAB, directionCD, frequencies;

				amplitude.x = gerstner4.wave0.amplitude;
				frequencies.x = gerstner4.wave0.frequency;
				directionAB.x = gerstner4.wave0.direction.x;
				directionAB.y = gerstner4.wave0.direction.y;

				amplitude.y = gerstner4.wave1.amplitude;
				frequencies.y = gerstner4.wave1.frequency;
				directionAB.z = gerstner4.wave1.direction.x;
				directionAB.w = gerstner4.wave1.direction.y;

				amplitude.z = gerstner4.wave2.amplitude;
				frequencies.z = gerstner4.wave2.frequency;
				directionCD.x = gerstner4.wave2.direction.x;
				directionCD.y = gerstner4.wave2.direction.y;

				amplitude.w = gerstner4.wave3.amplitude;
				frequencies.w = gerstner4.wave3.frequency;
				directionCD.z = gerstner4.wave3.direction.x;
				directionCD.w = gerstner4.wave3.direction.y;

				block.SetVector("_GrAB" + index, directionAB);
				block.SetVector("_GrCD" + index, directionCD);
				block.SetVector("_GrAmp" + index, amplitude);
				block.SetVector("_GrFrq" + index, frequencies);
			}

			// zero unused waves
			for(int index = gerstnerFours.Length; index < 5; ++index)
				block.SetVector("_GrAmp" + index, Vector4.zero);
		}

		public void OnWaterRender(Camera camera)
		{
			if(!Application.isPlaying || !enabled) return;

			UpdateWaves();
		}

		public void OnWaterPostRender(Camera camera)
		{

		}

		public void BuildShaderVariant(ShaderVariant variant, Water water, WindWaves windWaves, WaterQualityLevel qualityLevel)
		{
			variant.SetUnityKeyword("_WAVES_GERSTNER", enabled);
		}

		private void UpdateWaves()
		{
			int frameCount = Time.frameCount;

			if(lastUpdateFrame == frameCount)
				return;         // it's already done

			lastUpdateFrame = frameCount;

			var block = water.Renderer.PropertyBlock;
			float t = Time.time;

			for(int index = 0; index < gerstnerFours.Length; ++index)
			{
				var gerstner4 = gerstnerFours[index];

				Vector4 offset;
				offset.x = gerstner4.wave0.offset + gerstner4.wave0.speed * t;
				offset.y = gerstner4.wave1.offset + gerstner4.wave1.speed * t;
				offset.z = gerstner4.wave2.offset + gerstner4.wave2.speed * t;
				offset.w = gerstner4.wave3.offset + gerstner4.wave3.speed * t;

				block.SetVector("_GrOff" + index, offset);
			}
		}

		private void OnProfilesChanged(Water water)
		{
			FindMostMeaningfulWaves();
		}
	}

	public class Gerstner4
	{
		public GerstnerWave wave0;
		public GerstnerWave wave1;
		public GerstnerWave wave2;
		public GerstnerWave wave3;

		public Gerstner4(GerstnerWave wave0, GerstnerWave wave1, GerstnerWave wave2, GerstnerWave wave3)
		{
			this.wave0 = wave0;
			this.wave1 = wave1;
			this.wave2 = wave2;
			this.wave3 = wave3;
		}
	}

	public class GerstnerWave
	{
		public Vector2 direction;
		public float amplitude;
		public float offset;
		public float frequency;
		public float speed;

		public GerstnerWave()
		{
			direction = new Vector2(0, 1);
			frequency = 1;
		}

		public GerstnerWave(WaterWave wave, Vector2[] scaleOffsets)
		{
			float speed = wave.w;
			float mapOffset = (scaleOffsets[wave.scaleIndex].x * wave.nkx + scaleOffsets[wave.scaleIndex].y * wave.nky) * wave.k;       // match Gerstner to FFT map equivalent

			this.direction = new Vector2(wave.nkx, wave.nky);
			this.amplitude = wave.amplitude;
			this.offset = mapOffset + wave.offset;
			this.frequency = wave.k;
			this.speed = speed;
		}

		public GerstnerWave(Vector2 direction, float amplitude, float offset, float frequency, float speed)
		{
			this.direction = direction;
			this.amplitude = amplitude;
			this.offset = offset;
			this.frequency = frequency;
			this.speed = speed;
		}
	}
}
