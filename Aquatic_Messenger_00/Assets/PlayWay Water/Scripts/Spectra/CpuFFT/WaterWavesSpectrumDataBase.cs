using UnityEngine;
using System.Linq;

namespace PlayWay.Water
{
	public abstract class WaterWavesSpectrumDataBase
	{
		// 1. scale index 2. spectrum data
		private Vector3[][] spectrumValues;

		// 1. scale index 2. standard deviation for each mip level
		private float[][] standardDeviationData;

		// cpu waves
		private WaterWave[] cpuWaves;

		// shoreline waves
		private WaterWave[] shorelineCandidates;

		private bool cpuWavesDirty = true;
		private Vector2 lastWindDirection;
		private float stdDev;
		private float weight;
		private Vector2 weatherSystemOffset;
		private float weatherSystemRadius;
		private Vector2 windDirection = new Vector2(1.0f, 0.0f);
		private Texture2D texture;

		private readonly float tileSize;
		private readonly float gravity;
		protected readonly Water water;
		protected readonly WindWaves windWaves;

		protected WaterWavesSpectrumDataBase(Water water, WindWaves windWaves, float tileSize, float gravity)
		{
			this.water = water;
			this.windWaves = windWaves;
			this.tileSize = tileSize;
			this.gravity = gravity;
		}

		public WaterWave[] CpuWaves
		{
			get { return cpuWaves; }
		}

		public WaterWave[] ShorelineCandidates
		{
			get { return shorelineCandidates; }
		}

		public Vector3[][] SpectrumValues
		{
			get { return spectrumValues; }
		}

		public Texture2D Texture
		{
			get
			{
				if(texture == null)
					CreateSpectrumTexture();

				return texture;
			}
		}

		public float Weight
		{
			get { return weight; }
			set { weight = value; }
		}

		public float Gravity
		{
			get { return gravity; }
		}

		public Vector2 WeatherSystemOffset
		{
			get { return weatherSystemOffset; }
			set { weatherSystemOffset = value; }
		}

		public float WeatherSystemRadius
		{
			get { return weatherSystemRadius; }
			set { weatherSystemRadius = value; }
		}

		/// <summary>
		/// Applies only to non-local weather systems.
		/// </summary>
		public Vector2 WindDirection
		{
			get { return windDirection; }
			set { windDirection = value; }
		}

		private void CreateSpectrumTexture()
		{
			ValidateSpectrumData();

			int resolution = windWaves.FinalResolution;
			int halfResolution = resolution >> 1;

			// create texture
			texture = new Texture2D(resolution << 1, resolution << 1, TextureFormat.RGBAFloat, false, true)
			{
				hideFlags = HideFlags.DontSave,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Repeat
			};

			for(int scaleIndex = 0; scaleIndex < 4; ++scaleIndex)
			{
				var data = spectrumValues[scaleIndex];
				int uOffset = (scaleIndex & 1) == 0 ? 0 : resolution;
				int vOffset = (scaleIndex & 2) == 0 ? 0 : resolution;

				// fill texture
				for(int x = 0; x < resolution; ++x)
				{
					int u = (x + halfResolution) % resolution;

					for(int y = 0; y < resolution; ++y)
					{
						int v = (y + halfResolution) % resolution;

						Vector3 s = data[v * resolution + u];
						texture.SetPixel(uOffset + u, vOffset + v, new Color(s.x, s.y, s.z, 1.0f));
					}
				}
			}

			texture.Apply(false, true);
		}

		private void AnalyzeSpectrum()
		{
			int resolution = windWaves.FinalResolution;
			int halfResolution = resolution >> 1;
			int currentResolutionIndex = Mathf.RoundToInt(Mathf.Log(resolution >> 1, 2)) - 4;
			var shorelineCandidatesHeap = new Heap<WaterWave>();
			var importantWavesHeap = new Heap<WaterWave>();
			stdDev = 0.0f;

			for(byte scaleIndex = 0; scaleIndex < 4; ++scaleIndex)
			{
				var data = spectrumValues[scaleIndex];
				var scaleStandardDeviationData = standardDeviationData[scaleIndex] = new float[currentResolutionIndex + 1];

				float frequencyScale = 2.0f * Mathf.PI / tileSize;
				float offsetX = tileSize + (0.5f / resolution) * tileSize;
				float offsetZ = -tileSize + (0.5f / resolution) * tileSize;

				for(int x = 0; x < resolution; ++x)
				{
					float kx = frequencyScale * (x - halfResolution);
					ushort u = (ushort)((x + halfResolution) % resolution);
					ushort offset = (ushort)(u*resolution);

					for(int y = 0; y < resolution; ++y)
					{
						float ky = frequencyScale * (y - halfResolution);
						ushort v = (ushort)((y + halfResolution) % resolution);

						Vector3 s = data[offset + v];
						float ls = s.x*s.x + s.y*s.y;
						float amplitude = Mathf.Sqrt(ls);
						float k = Mathf.Sqrt(kx * kx + ky * ky);
						float w = Mathf.Sqrt(gravity * k);

						// collect important waves
						if(amplitude >= 0.0025f)
						{
							float cpuPriority = amplitude;

							if(cpuPriority < 0)
								cpuPriority = -cpuPriority;

							importantWavesHeap.Insert(new WaterWave(scaleIndex, offsetX, offsetZ, u, v, kx, ky, k, w, amplitude, cpuPriority));

							if (importantWavesHeap.Count > 100)
								importantWavesHeap.ExtractMax();
						}

						// collect shoreline candidates
						if(amplitude > 0.025f)
						{
							float shorelinePriority = k / amplitude;            // it's used in a max-heap, so this is an inverse of a real priority
							shorelineCandidatesHeap.Insert(new WaterWave(scaleIndex, offsetX, offsetZ, u, v, kx, ky, k, w, amplitude, shorelinePriority));

							if(shorelineCandidatesHeap.Count > 200)
								shorelineCandidatesHeap.ExtractMax();
						}

						// compute total (halved) elevation variance per mip level
						int mipIndex = GetMipIndex(Mathf.Max(Mathf.Min(u, resolution - u - 1), Mathf.Min(v, resolution - v - 1)));
						scaleStandardDeviationData[mipIndex] += ls;
					}
				}

				// half of variance to standard deviation
				for (int i = 0; i < scaleStandardDeviationData.Length; ++i)
				{
					scaleStandardDeviationData[i] = Mathf.Sqrt(2.0f*scaleStandardDeviationData[i]);
					stdDev += scaleStandardDeviationData[i];
				}
			}

			cpuWaves = importantWavesHeap.ToArray();
			SortCpuWaves(cpuWaves, false);

			shorelineCandidates = shorelineCandidatesHeap.ToArray();
			System.Array.Sort(shorelineCandidates);
		}

		public static int GetMipIndex(int i)
		{
			if(i == 0) return 0;

			int mip = (int)Mathf.Log(i, 2) - 3;

			return mip >= 0 ? mip : 0;
		}

		public float GetStandardDeviation()
		{
			return stdDev;
		}

		public float GetStandardDeviation(int scaleIndex, int mipLevel)
		{
			var data = standardDeviationData[scaleIndex];
			return mipLevel < data.Length ? data[mipLevel] : 0.0f;
		}

		public void SetCpuWavesDirty()
		{
			cpuWavesDirty = true;
		}

		public void ValidateSpectrumData()
		{
			if(cpuWaves != null)
				return;

			lock (this)
			{
				if(cpuWaves != null)
					return;

				if (spectrumValues == null)
				{
					spectrumValues = new Vector3[4][];
					standardDeviationData = new float[4][];
				}

				int resolution = windWaves.FinalResolution;
				int resolutionSquared = resolution*resolution;
				int currentResolutionIndex = Mathf.RoundToInt(Mathf.Log(resolution, 2)) - 4;

				if (spectrumValues[0] == null || spectrumValues[0].Length != resolutionSquared)
				{
					for(int i = 0; i < 4; ++i)
					{
						spectrumValues[i] = new Vector3[resolutionSquared];
						standardDeviationData[i] = new float[currentResolutionIndex + 1];
					}
				}
				
				GenerateContents(spectrumValues);
				AnalyzeSpectrum();
			}
		}

		public void UpdateSpectralValues(Vector2 windDirection, float directionality)
		{
			ValidateSpectrumData();

			if(cpuWavesDirty)
			{
				lock(this)
				{
					if(cpuWaves == null || !cpuWavesDirty) return;

					lock (cpuWaves)
					{
						cpuWavesDirty = false;

						float directionalityInv = 1.0f - directionality;
						float horizontalDisplacementScale = water.Materials.HorizontalDisplacementScale;
						int resolution = windWaves.FinalResolution;
						bool mostlySorted = Vector2.Dot(lastWindDirection, windDirection) >= 0.97f;

						var cpuWavesLocal = this.cpuWaves;
						
						for (int i = 0; i < cpuWavesLocal.Length; ++i)
							cpuWavesLocal[i].UpdateSpectralValues(spectrumValues, windDirection, directionalityInv, resolution, horizontalDisplacementScale);

						SortCpuWaves(cpuWavesLocal, mostlySorted);

						var shorelineCandidatesLocal = this.shorelineCandidates;

						for (int i = 0; i < shorelineCandidatesLocal.Length; ++i)
							shorelineCandidatesLocal[i].UpdateSpectralValues(spectrumValues, windDirection, directionalityInv, resolution, horizontalDisplacementScale);

						lastWindDirection = windDirection;
					}
				}

			}
		}

		public static void SortCpuWaves(WaterWave[] windWaves, bool mostlySorted)
		{
			if(!mostlySorted)
			{
				System.Array.Sort(windWaves, (a, b) =>
				{
					if(a.cpuPriority > b.cpuPriority)
						return -1;
					else
						return a.cpuPriority == b.cpuPriority ? 0 : 1;
				});
			}
			else
			{
				// bubble sort
				int numCpuWaves = windWaves.Length;
				int prevIndex = 0;

				for(int index = 1; index < numCpuWaves; ++index)
				{
					if(windWaves[prevIndex].cpuPriority < windWaves[index].cpuPriority)
					{
						var t = windWaves[prevIndex];
						windWaves[prevIndex] = windWaves[index];
						windWaves[index] = t;

						if(index != 1) index -= 2;
					}

					prevIndex = index;
				}
			}
		}

		public void Dispose(bool onlyTexture)
		{
			if(texture != null)
			{
				texture.Destroy();
				texture = null;
			}

			if(!onlyTexture)
			{
				lock(this)
				{
					spectrumValues = null;
					cpuWaves = null;
					cpuWavesDirty = true;
				}
			}
		}

		protected abstract void GenerateContents(Vector3[][] spectrumValues);
	}
}
