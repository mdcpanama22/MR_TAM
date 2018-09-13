using System.Collections.Generic;
using System.Linq;
using UnityEngine;

#if WATER_SIMD
using vector4 = Mono.Simd.Vector4f;
#else
using vector4 = UnityEngine.Vector4;
#endif

namespace PlayWay.Water
{
	public class SpectrumResolverCPU
	{
		private WaterTileSpectrum[] tileSpectra;
		private Vector2 surfaceOffset;
		private Vector2 windDirection;
		private float lastFrameTime;
		private float uniformWaterScale;
		private WaterWave[] directWaves;
		private float stdDev;
		private int targetDirectWavesCount = -1;
		private int cachedSeed;
		private bool cpuWavesDirty;

		// statistical data
		private float maxVerticalDisplacement;
		private float maxHorizontalDisplacement;

		private readonly int numTiles;
		private readonly Water water;
		private readonly WindWaves windWaves;
		private readonly List<WaterWavesSpectrumDataBase> spectraDataList;
		protected readonly List<WaterWavesSpectrumDataBase> overlayedSpectra;
		protected Dictionary<WaterWavesSpectrum, WaterWavesSpectrumData> spectraDataCache;

		public SpectrumResolverCPU(Water water, WindWaves windWaves, int numScales)
		{
			this.water = water;
			this.windWaves = windWaves;
			this.spectraDataCache = new Dictionary<WaterWavesSpectrum, WaterWavesSpectrumData>();
			this.spectraDataList = new List<WaterWavesSpectrumDataBase>();
			this.overlayedSpectra = new List<WaterWavesSpectrumDataBase>();
            this.numTiles = numScales;
			this.cachedSeed = water.Seed;

			CreateSpectraLevels();
		}

		public WaterWave[] DirectWaves
		{
			get
			{
				lock (this)
				{
					return GetValidatedDirectWavesList();
				}
			}
		}

		public float MaxVerticalDisplacement
		{
			get { return maxVerticalDisplacement; }
		}

		public float MaxHorizontalDisplacement
		{
			get { return maxHorizontalDisplacement; }
		}
		
		public Vector2 WindDirection
		{
			get { return windDirection; }
		}

		public float LastFrameTime
		{
			get { return lastFrameTime; }
		}

		public float StdDev
		{
			get { return stdDev; }
		}

		public WaterTileSpectrum GetTile(int index)
		{
			return tileSpectra[index];
        }
		
		internal void Update()
		{
			surfaceOffset = water.SurfaceOffset;
			lastFrameTime = water.Time;
			uniformWaterScale = water.UniformWaterScale;

			UpdateCachedSeed();
			
			bool allowFFT = WaterProjectSettings.Instance.AllowCpuFFT;

			for(int scaleIndex=0; scaleIndex < numTiles; ++scaleIndex)
			{
				int fftResolution = 16;
				int mipLevel = 0;

				while(true)
				{
					float totalStdDev = 0;

					for(int spectrumIndex = spectraDataList.Count - 1; spectrumIndex >= 0; --spectrumIndex)
					{
						var spectrum = spectraDataList[spectrumIndex];
						spectrum.ValidateSpectrumData();

						var stdDev = spectrum.GetStandardDeviation(scaleIndex, mipLevel);
						totalStdDev += stdDev * spectrum.Weight;
					}

					for(int spectrumIndex = overlayedSpectra.Count - 1; spectrumIndex >= 0; --spectrumIndex)
					{
						var spectrum = overlayedSpectra[spectrumIndex];
						spectrum.ValidateSpectrumData();

						var stdDev = spectrum.GetStandardDeviation(scaleIndex, mipLevel);
						totalStdDev += stdDev * spectrum.Weight;
					}

					if(totalStdDev < windWaves.CpuDesiredStandardError * 0.25f || fftResolution >= windWaves.FinalResolution)			// desired error is multiplied by 0.25 because there are 4 scales that cumulate the standard error
						break;

					fftResolution <<= 1;
					++mipLevel;
				}
				
				if(fftResolution > windWaves.FinalResolution)
					fftResolution = windWaves.FinalResolution;

				var level = tileSpectra[scaleIndex];

				if(level.SetResolveMode(fftResolution >= 16 && allowFFT, fftResolution))
					cpuWavesDirty = true;
			}

#if WATER_DEBUG
			DebugUpdate();
#endif
		}

		internal void SetWindDirection(Vector2 windDirection)
		{
			this.windDirection = windDirection;
			SetDirectionalSpectrumDirty();
		}

		public List<WaterWavesSpectrumDataBase> GetOverlayedSpectraDirect()
		{
			return overlayedSpectra;
		}

		public void DisposeCachedSpectra()
		{
			lock(spectraDataCache)
			{
				var kv = spectraDataCache.GetEnumerator();

				while(kv.MoveNext())
					kv.Current.Value.Dispose(false);
			}

			SetDirectionalSpectrumDirty();
        }
		
		public WaterWavesSpectrumData GetSpectrumData(WaterWavesSpectrum spectrum)
		{
			WaterWavesSpectrumData spectrumData;

			if(!spectraDataCache.TryGetValue(spectrum, out spectrumData))
			{
				lock (spectraDataCache)
				{
					spectraDataCache[spectrum] = spectrumData = new WaterWavesSpectrumData(water, windWaves, spectrum);
				}

				spectrumData.ValidateSpectrumData();
				cpuWavesDirty = true;

				lock (spectraDataList)
				{
					spectraDataList.Add(spectrumData);
				}
			}

			return spectrumData;
		}

		/// <summary>
		/// Add additional spectrum into the mix that doesn't come from used water profiles. It may be some distant storm spectrum or a custom overlay.
		/// </summary>
		/// <param name="spectrum"></param>
		public virtual void AddSpectrum(WaterWavesSpectrumDataBase spectrum)
		{
			lock (overlayedSpectra)
			{
				overlayedSpectra.Add(spectrum);
			}
		}

		/// <summary>
		/// Remove additional spectrum into the mix that doesn't come from used water profiles. It may be some distant storm spectrum or a custom overlay.
		/// </summary>
		/// <param name="spectrum"></param>
		public virtual void RemoveSpectrum(WaterWavesSpectrumDataBase spectrum)
		{
			lock (overlayedSpectra)
			{
				overlayedSpectra.Remove(spectrum);
			}
		}

		public bool ContainsSpectrum(WaterWavesSpectrumDataBase spectrum)
		{
			return overlayedSpectra.Contains(spectrum);
		}

		public void CacheSpectrum(WaterWavesSpectrum spectrum)
		{
			GetSpectrumData(spectrum);
		}

		public Dictionary<WaterWavesSpectrum, WaterWavesSpectrumData> GetCachedSpectraDirect()
		{
			return spectraDataCache;
		}

		#region WavemapsSampling
		private void InterpolationParams(float x, float z, int scaleIndex, float tileSize, out float fx, out float invFx, out float fy, out float invFy, out int index0, out int index1, out int index2, out int index3)
		{
			int resolution = tileSpectra[scaleIndex].ResolutionFFT;
			int displayResolution = windWaves.FinalResolution;
			x += (0.5f / displayResolution) * tileSize;
			z += (0.5f / displayResolution) * tileSize;

			float multiplier = resolution / tileSize;
			fx = x * multiplier;
			fy = z * multiplier;
			int indexX = (int)fx; if(indexX > fx) --indexX;     // inlined FastMath.FloorToInt(fx);
			int indexY = (int)fy; if(indexY > fy) --indexY;     // inlined FastMath.FloorToInt(fy);
			fx -= indexX;
			fy -= indexY;

			indexX = indexX % resolution;
			indexY = indexY % resolution;

			if(indexX < 0) indexX += resolution;
			if(indexY < 0) indexY += resolution;

			indexX = resolution - indexX - 1;
			indexY = resolution - indexY - 1;

			int indexX_2 = indexX + 1;
			int indexY_2 = indexY + 1;

			if(indexX_2 == resolution) indexX_2 = 0;
			if(indexY_2 == resolution) indexY_2 = 0;

			indexY *= resolution;
			indexY_2 *= resolution;

			index0 = indexY + indexX;
			index1 = indexY + indexX_2;
			index2 = indexY_2 + indexX;
			index3 = indexY_2 + indexX_2;

			invFx = 1.0f - fx;
			invFy = 1.0f - fy;
		}

		public Vector3 GetDisplacementAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			Vector3 result = new Vector3();
			x = -(x + surfaceOffset.x);
			z = -(z + surfaceOffset.y);
			
			if(targetDirectWavesCount == -1)
			{
				// sample FFT results
				for(int scaleIndex = numTiles - 1; scaleIndex >= 0; --scaleIndex)
				{
					if(tileSpectra[scaleIndex].resolveByFFT)
					{
						float fx, invFx, fy, invFy, t; int index0, index1, index2, index3;
						Vector2[] da, db; vector4[] fa, fb;

						lock (tileSpectra[scaleIndex])
						{
							InterpolationParams(x, z, scaleIndex, windWaves.TileSizes[scaleIndex], out fx, out invFx, out fy, out invFy, out index0, out index1, out index2, out index3);
							tileSpectra[scaleIndex].GetResults(time, out da, out db, out fa, out fb, out t);
						}

						Vector2 subResult = FastMath.Interpolate(
							ref da[index0], ref da[index1], ref da[index2], ref da[index3],
							ref db[index0], ref db[index1], ref db[index2], ref db[index3],
							fx, invFx, fy, invFy, t
						);

						result.x -= subResult.x;
						result.z -= subResult.y;

#if WATER_SIMD
						result.y += FastMath.Interpolate(
							fa[index0].W, fa[index1].W, fa[index2].W, fa[index3].W,
							fb[index0].W, fb[index1].W, fb[index2].W, fb[index3].W,
							fx, invFx, fy, invFy, t
						);
#else
						result.y += FastMath.Interpolate(
							fa[index0].w, fa[index1].w, fa[index2].w, fa[index3].w,
							fb[index0].w, fb[index1].w, fb[index2].w, fb[index3].w,
							fx, invFx, fy, invFy, t
						);
#endif
					}
				}
			}
			else
			{
				// sample waves directly
				lock(this)
				{
					var waves = GetValidatedDirectWavesList();
					int startIndex = (int)(spectrumStart * waves.Length);
					int endIndex = (int)(spectrumEnd * waves.Length);

					if(startIndex != endIndex)
					{
						Vector3 subResult = new Vector3();

						for(int i = startIndex; i < endIndex; ++i)
							subResult += waves[i].GetDisplacementAt(x, z, time);

						result += subResult;
					}
				}
			}

			float horizontalScale = -water.Materials.HorizontalDisplacementScale * uniformWaterScale;
			result.x = result.x * horizontalScale;
			result.y *= uniformWaterScale;
            result.z = result.z * horizontalScale;

			return result;
		}

		public Vector2 GetHorizontalDisplacementAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			Vector2 result = new Vector2();
			x = -(x + surfaceOffset.x);
			z = -(z + surfaceOffset.y);

			// sample FFT results
			if(targetDirectWavesCount == -1)
			{
				for(int scaleIndex = numTiles - 1; scaleIndex >= 0; --scaleIndex)
				{
					if(tileSpectra[scaleIndex].resolveByFFT)
					{
						float fx, invFx, fy, invFy, t; int index0, index1, index2, index3;
						Vector2[] da, db;

						lock (tileSpectra[scaleIndex])
						{
							vector4[] fa, fb;
							InterpolationParams(x, z, scaleIndex, windWaves.TileSizes[scaleIndex], out fx, out invFx, out fy, out invFy, out index0, out index1, out index2, out index3);
							tileSpectra[scaleIndex].GetResults(time, out da, out db, out fa, out fb, out t);
						}
						
						result -= FastMath.Interpolate(
							ref da[index0], ref da[index1], ref da[index2], ref da[index3],
							ref db[index0], ref db[index1], ref db[index2], ref db[index3],
							fx, invFx, fy, invFy, t
						);
					}
				}
			}
			else
			{
				// sample waves directly
				lock(this)
				{
					var waves = GetValidatedDirectWavesList();
					int startIndex = (int)(spectrumStart * waves.Length);
					int endIndex = (int)(spectrumEnd * waves.Length);

					if(startIndex != endIndex)
					{
						Vector2 subResult = new Vector2();

						for(int i = startIndex; i < endIndex; ++i)
							subResult += waves[i].GetRawHorizontalDisplacementAt(x, z, time);

						result += subResult;
					}
				}
			}

			float horizontalScale = -water.Materials.HorizontalDisplacementScale * uniformWaterScale;
			result.x = result.x * horizontalScale;
			result.y = result.y * horizontalScale;

			return result;
		}

		public Vector4 GetForceAndHeightAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			vector4 result = new vector4();
			x = -(x + surfaceOffset.x);
			z = -(z + surfaceOffset.y);

			// sample FFT results
			if(targetDirectWavesCount == -1)
			{
				for(int scaleIndex = numTiles - 1; scaleIndex >= 0; --scaleIndex)
				{
					if(tileSpectra[scaleIndex].resolveByFFT)
					{
						float fx, invFx, fy, invFy, t; int index0, index1, index2, index3;
						vector4[] fa, fb;

						lock (tileSpectra[scaleIndex])
						{
							Vector2[] da, db;
							InterpolationParams(x, z, scaleIndex, windWaves.TileSizes[scaleIndex], out fx, out invFx, out fy, out invFy, out index0, out index1, out index2, out index3);
							tileSpectra[scaleIndex].GetResults(time, out da, out db, out fa, out fb, out t);
						}
						
						result += FastMath.Interpolate(
							fa[index0], fa[index1], fa[index2], fa[index3],
							fb[index0], fb[index1], fb[index2], fb[index3],
							fx, invFx, fy, invFy, t
						);
					}
				}
			}
			else
			{
				// sample waves directly
				lock(this)
				{
					var waves = GetValidatedDirectWavesList();
					int startIndex = (int)(spectrumStart * waves.Length);
					int endIndex = (int)(spectrumEnd * waves.Length);

					if(startIndex != endIndex)
					{
						Vector4 subResult = new Vector4();

						for(int i = startIndex; i < endIndex; ++i)
							waves[i].GetForceAndHeightAt(x, z, time, ref subResult);

#if WATER_SIMD
						result += new vector4(subResult.x, subResult.y, subResult.z, subResult.w);
#else
						result += subResult;
#endif
					}
				}
			}

			float horizontalScale = -water.Materials.HorizontalDisplacementScale * uniformWaterScale;

#if WATER_SIMD
			return new Vector4(result.X * horizontalScale, result.Y * 0.5f, result.Z * horizontalScale, result.W);
#else
			result.x = result.x * horizontalScale;
			result.z = result.z * horizontalScale;
			result.y *= 0.5f * uniformWaterScale;
			result.w *= uniformWaterScale;				// not 100% sure about this

			return result;
#endif
		}

		public float GetHeightAt(float x, float z, float spectrumStart, float spectrumEnd, float time)
		{
			float result = 0.0f;
			x = -(x + surfaceOffset.x);
			z = -(z + surfaceOffset.y);
			
			if(targetDirectWavesCount == -1)
			{
				// sample FFT results
				for(int scaleIndex = numTiles - 1; scaleIndex >= 0; --scaleIndex)
				{
					if(tileSpectra[scaleIndex].resolveByFFT)
					{
						float fx, invFx, fy, invFy, t; int index0, index1, index2, index3;
						vector4[] fa, fb;

						lock (tileSpectra[scaleIndex])
						{
							Vector2[] da, db;
							InterpolationParams(x, z, scaleIndex, windWaves.TileSizes[scaleIndex], out fx, out invFx, out fy, out invFy, out index0, out index1, out index2, out index3);
							tileSpectra[scaleIndex].GetResults(time, out da, out db, out fa, out fb, out t);
						}

#if WATER_SIMD
						result += FastMath.Interpolate(
							fa[index0].W, fa[index1].W, fa[index2].W, fa[index3].W,
							fb[index0].W, fb[index1].W, fb[index2].W, fb[index3].W,
							fx, invFx, fy, invFy, t
						);
#else
						result += FastMath.Interpolate(
							fa[index0].w, fa[index1].w, fa[index2].w, fa[index3].w,
							fb[index0].w, fb[index1].w, fb[index2].w, fb[index3].w,
							fx, invFx, fy, invFy, t
						);
#endif
					}
				}
			}
			else
			{
				// sample waves directly
				lock(this)
				{
					var waves = GetValidatedDirectWavesList();
					int startIndex = (int)(spectrumStart * waves.Length);
					int endIndex = (int)(spectrumEnd * waves.Length);

					if (startIndex != endIndex)
					{
						float subResult = 0.0f;

						for(int i = startIndex; i < endIndex; ++i)
							subResult += waves[i].GetHeightAt(x, z, time);

						result += subResult;
					}
				}
			}

			return result * uniformWaterScale;
		}
#endregion

		#region WavesSelecting

		public void SetDirectWaveEvaluationMode(int waveCount)
		{
			lock (this)
			{
				if (directWaves == null)
					directWaves = new WaterWave[0];

				targetDirectWavesCount = waveCount;
				cpuWavesDirty = true;
			}
		}

		public void SetFFTEvaluationMode()
		{
			lock (this)
			{
				directWaves = null;
				targetDirectWavesCount = 0;
			}
		}

		public WaterWave[] FindMostMeaningfulWaves(int waveCount)
		{
			var waves = new Heap<WaterWave>();

			for(int spectrumDataIndex = spectraDataList.Count - 1; spectrumDataIndex >= 0; --spectrumDataIndex)
			{
				var spectrumData = spectraDataList[spectrumDataIndex];
				spectrumData.UpdateSpectralValues(windDirection, windWaves.SpectrumDirectionality);

				lock(spectrumData)
				{
					float weight = spectrumData.Weight;
					var cpuWaves = spectrumData.CpuWaves;

					for(int i = 0; i < cpuWaves.Length; ++i)
					{
						var wave = cpuWaves[i];
						wave.amplitude *= weight;
						wave.cpuPriority *= weight;
						waves.Insert(wave);

						if(waves.Count > waveCount)
							waves.ExtractMax();
					}
				}
			}

			return waves.ToArray();
		}

		private WaterWave[] GetValidatedDirectWavesList()
		{
			if(cpuWavesDirty)
			{
				cpuWavesDirty = false;
				var waves = FindMostMeaningfulWaves(targetDirectWavesCount);
				directWaves = waves.ToArray();
			}

			return directWaves;
		}

		public GerstnerWave[] SelectShorelineWaves(int waveCount, float angle, float coincidenceRange)
		{
			var waves = new Heap<WaterWave>();

			for(int spectrumDataIndex = spectraDataList.Count - 1; spectrumDataIndex >= 0; --spectrumDataIndex)
			{
				var spectrumData = spectraDataList[spectrumDataIndex];
				spectrumData.UpdateSpectralValues(windDirection, windWaves.SpectrumDirectionality);

				lock(spectrumData)
				{
					float weight = spectrumData.Weight;
					var shorelineCandidates = spectrumData.ShorelineCandidates;

					for(int i = 0; i < shorelineCandidates.Length; ++i)
					{
						var wave = shorelineCandidates[i];
						wave.amplitude *= weight;
						wave.cpuPriority *= weight;

						float waveAngle = Mathf.Atan2(wave.nkx, wave.nky) * Mathf.Rad2Deg;

						if (Mathf.Abs(Mathf.DeltaAngle(waveAngle, angle)) < coincidenceRange && wave.amplitude > 0.025f)
						{
							waves.Insert(wave);

							if (waves.Count > waveCount)
								waves.ExtractMax();
						}
					}
				}
			}

			var offsets = new Vector2[4];

			for(int i = 0; i < 4; ++i)
			{
				float tileSize = windWaves.TileSizes[i];

				offsets[i].x = tileSize + (0.5f / windWaves.FinalResolution) * tileSize;
				offsets[i].y = -tileSize + (0.5f / windWaves.FinalResolution) * tileSize;
			}

			var wavesArray = waves.ToArray();
			int c = Mathf.Min(waves.Count, waveCount);
			var gerstners = new GerstnerWave[c];

			for(int i = 0; i < c; ++i)
				gerstners[i] = new GerstnerWave(wavesArray[waves.Count - i - 1], offsets);            // shoreline waves have a reversed order here...

			return gerstners;
		}
		#endregion

		private void UpdateCachedSeed()
		{
			if(cachedSeed != water.Seed)
			{
				cachedSeed = water.Seed;
				DisposeCachedSpectra();
				OnProfilesChanged();
			}
		}

		internal virtual void OnProfilesChanged()
		{
			var profiles = water.ProfilesManager.Profiles;

			var kv = spectraDataCache.GetEnumerator();
			while (kv.MoveNext())
				kv.Current.Value.Weight = 0.0f;

			for(int i=0; i<profiles.Length; ++i)
			{
				var weightedProfile = profiles[i];

				if(weightedProfile.Weight <= 0.0001f)
					continue;

				var spectrum = weightedProfile.Profile.Spectrum;

				WaterWavesSpectrumData spectrumData;

				if(!spectraDataCache.TryGetValue(spectrum, out spectrumData))
					spectrumData = GetSpectrumData(spectrum);

				spectrumData.Weight = weightedProfile.Weight;
			}

			SetDirectionalSpectrumDirty();

			stdDev = 0.0f;
			float worstCasesFactor = 0.0f;

			kv = spectraDataCache.GetEnumerator();
			while (kv.MoveNext())
			{
				var spectrumData = kv.Current.Value;
				spectrumData.ValidateSpectrumData();

				stdDev += spectrumData.GetStandardDeviation()*spectrumData.Weight;

				if(spectrumData.CpuWaves.Length != 0)
					worstCasesFactor += spectrumData.CpuWaves[0].amplitude * spectrumData.Weight;
			}

			for (int i = overlayedSpectra.Count - 1; i >= 0; --i)
			{
				var spectrumData = overlayedSpectra[i];
				spectrumData.ValidateSpectrumData();

				stdDev += spectrumData.GetStandardDeviation()*spectrumData.Weight;

				if(spectrumData.CpuWaves.Length != 0)
					worstCasesFactor += spectrumData.CpuWaves[0].amplitude*spectrumData.Weight;
			}

			maxVerticalDisplacement = stdDev * 1.6f + worstCasesFactor;
			maxHorizontalDisplacement = maxVerticalDisplacement * water.Materials.HorizontalDisplacementScale;
		}
		
		private void CreateSpectraLevels()
		{
			this.tileSpectra = new WaterTileSpectrum[numTiles];

			for(int scaleIndex = 0; scaleIndex < numTiles; ++scaleIndex)
				tileSpectra[scaleIndex] = new WaterTileSpectrum(water, windWaves, scaleIndex);
		}

#if WATER_DEBUG
		private void DebugUpdate()
		{
			if(Input.GetKeyDown(KeyCode.F10))
			{
				float scale = 0.1f;

				for(int i = 0; i < 4; ++i)
				{
					if(!tileSpectra[i].IsResolvedByFFT) continue;

					int resolution = tileSpectra[i].ResolutionFFT;

					lock (tileSpectra[i])
					{
						var tex = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false, true);
						for(int y = 0; y < resolution; ++y)
						{
							for(int x = 0; x < resolution; ++x)
							{
								tex.SetPixel(x, y, new Color(tileSpectra[i].directionalSpectrum[y * resolution + x].x, tileSpectra[i].directionalSpectrum[y * resolution + x].y, 0.0f, 1.0f));
							}
						}

						tex.Apply();
						var bytes = tex.EncodeToPNG();
						System.IO.File.WriteAllBytes("CPU Dir " + i + ".png", bytes);

						tex.Destroy();
					}
				}

				for(int i = 0; i < 4; ++i)
				{
					if(!tileSpectra[i].IsResolvedByFFT) continue;

					int resolution = tileSpectra[i].ResolutionFFT;

					var tex = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false, true);
					for(int y = 0; y < resolution; ++y)
					{
						for(int x = 0; x < resolution; ++x)
						{
							tex.SetPixel(x, y, new Color(tileSpectra[i].displacements[1][y * resolution + x].x * water.HorizontalDisplacementScale * scale, tileSpectra[i].forceAndHeight[1][y * resolution + x][3] * scale, tileSpectra[i].displacements[1][y * resolution + x].y * water.HorizontalDisplacementScale * scale, 1.0f));
						}
					}

					tex.Apply();
					var bytes = tex.EncodeToPNG();
					System.IO.File.WriteAllBytes("CPU FFT " + i + ".png", bytes);

					tex.Destroy();
				}

				for(int i = 0; i < 4; ++i)
				{
					if(!tileSpectra[i].IsResolvedByFFT) continue;

					int resolution = tileSpectra[i].ResolutionFFT;

					var tex = new Texture2D(resolution, resolution, TextureFormat.ARGB32, false, true);
					for(int y = 0; y < resolution; ++y)
					{
						for(int x = 0; x < resolution; ++x)
						{
							Vector2 displacement = GetHorizontalDisplacementAt((float)(x + 0.5f) / resolution * windWaves.TileSizes[i], (float)(y + 0.5f) / resolution * windWaves.TileSizes[i], 0.0f, 1.0f, Time.time);
							float height = GetHeightAt((float)(x + 0.5f) / resolution * windWaves.TileSizes[i], (float)(y + 0.5f) / resolution * windWaves.TileSizes[i], 0.0f, 1.0f, Time.time);
							tex.SetPixel(x, y, new Color(displacement.x * scale, height * scale, displacement.y * scale, 1.0f));
						}
					}

					tex.Apply();
					var bytes = tex.EncodeToPNG();
					System.IO.File.WriteAllBytes("CPU FFT Sampled " + i + ".png", bytes);

					tex.Destroy();
				}
			}
		}
#endif

		public virtual void SetDirectionalSpectrumDirty()
		{
			cpuWavesDirty = true;

			for(int i=spectraDataList.Count-1; i>=0; --i)
				spectraDataList[i].SetCpuWavesDirty();

			for (int scaleIndex = 0; scaleIndex < numTiles; ++scaleIndex)
				tileSpectra[scaleIndex].SetDirty();
		}

		internal virtual void OnMapsFormatChanged(bool resolution)
		{
			if(spectraDataCache != null)
			{
				var kv = spectraDataCache.GetEnumerator();
				while(kv.MoveNext())
					kv.Current.Value.Dispose(!resolution);
			}

			if (resolution)
			{
				for (int i = overlayedSpectra.Count - 1; i >= 0; --i)
					overlayedSpectra[i].Dispose(false);
			}

			SetDirectionalSpectrumDirty();
		}

		internal virtual void OnDestroy()
		{
			OnMapsFormatChanged(true);
			spectraDataCache = null;

			for (int i = overlayedSpectra.Count - 1; i >= 0; --i)
				overlayedSpectra[i].Dispose(false);

			overlayedSpectra.Clear();

			lock(spectraDataList)
			{
				spectraDataList.Clear();
			}
		}
	}
}
