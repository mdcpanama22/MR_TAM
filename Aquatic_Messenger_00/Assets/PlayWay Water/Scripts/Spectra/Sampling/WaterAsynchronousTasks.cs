using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace PlayWay.Water
{
	public sealed class WaterAsynchronousTasks : MonoBehaviour
	{
		private static WaterAsynchronousTasks instance;

		public static WaterAsynchronousTasks Instance
		{
			get
			{
				if(instance == null)
				{
					instance = FindObjectOfType<WaterAsynchronousTasks>();

					if(instance == null)
					{
						var go = new GameObject("PlayWay Water Spectrum Sampler") {hideFlags = HideFlags.HideInHierarchy};
						instance = go.AddComponent<WaterAsynchronousTasks>();
					}
				}

				return instance;
			}
		}

		public static bool HasInstance
		{
			get { return instance != null; }
		}
		
		private bool run;

		private readonly List<WaterTileSpectrum> fftSpectra = new List<WaterTileSpectrum>();
		private int fftSpectrumIndex;
		private float fftTimeStep = 0.2f;

		private readonly List<WaterSample> computations = new List<WaterSample>();
		private int computationIndex;

		private System.Exception threadException;

		private void Awake()
		{
			run = true;

			if(!Application.isPlaying)
				return;

			for(int i = 0; i < WaterProjectSettings.Instance.PhysicsThreads; ++i)
			{
				Thread thread = new Thread(RunSamplingTask) {Priority = WaterProjectSettings.Instance.PhysicsThreadsPriority};
				thread.Start();
			}
			
			{
				Thread thread = new Thread(RunFFTTask) {Priority = WaterProjectSettings.Instance.PhysicsThreadsPriority};
				thread.Start();
			}
		}

		public void AddWaterSampleComputations(WaterSample computation)
		{
			lock(computations)
			{
				computations.Add(computation);
			}
		}

		public void RemoveWaterSampleComputations(WaterSample computation)
		{
			lock(computations)
			{
				int index = computations.IndexOf(computation);

				if(index == -1) return;

				if(index < computationIndex)
					--computationIndex;

				computations.RemoveAt(index);
			}
		}
		
		public void AddFFTComputations(WaterTileSpectrum scale)
		{
			lock(fftSpectra)
			{
				fftSpectra.Add(scale);
			}
		}

		public void RemoveFFTComputations(WaterTileSpectrum scale)
		{
			lock(fftSpectra)
			{
				int index = fftSpectra.IndexOf(scale);

				if(index == -1) return;

				if(index < fftSpectrumIndex)
					--fftSpectrumIndex;

				fftSpectra.RemoveAt(index);
			}
		}

		private void OnDisable()
		{
			run = false;

			if(threadException != null)
				UnityEngine.Debug.LogException(threadException);
		}

#if UNITY_EDITOR
		private void Update()
		{
			if(threadException != null)
			{
				UnityEngine.Debug.LogException(threadException);
				threadException = null;
            }
		}
#endif

		private void RunSamplingTask()
		{
			try
			{
				while(run)
				{
					WaterSample computation = null;

					lock (computations)
					{
						if(computations.Count != 0)
						{
							if(computationIndex >= computations.Count)
								computationIndex = 0;

							computation = computations[computationIndex++];
						}
					}

					if(computation == null)
					{
						Thread.Sleep(2);
						continue;
					}

					lock (computation)
					{
						computation.ComputationStep();
					}
				}
			}
			catch(System.Exception e)
			{
				threadException = e;
            }
		}

		private void RunFFTTask()
		{
			try
			{
				var fftTask = new CpuFFT();
				Stopwatch stopwatch = new Stopwatch();
				bool performanceProblems = false;

				while(run)
				{
					WaterTileSpectrum spectrum = null;

					lock (fftSpectra)
					{
						if(fftSpectra.Count != 0)
						{
							if(fftSpectrumIndex >= fftSpectra.Count)
								fftSpectrumIndex = 0;

							if(fftSpectrumIndex == 0)
							{
								if(stopwatch.ElapsedMilliseconds > fftTimeStep * 900.0f)
								{
									if(performanceProblems)
										fftTimeStep += 0.05f;
									else
										performanceProblems = true;
								}
								else
								{
									performanceProblems = false;

									if(fftTimeStep > 0.2f)
										fftTimeStep -= 0.001f;
								}

								stopwatch.Reset();
								stopwatch.Start();
							}

							spectrum = fftSpectra[fftSpectrumIndex++];
						}
					}

					if(spectrum == null)
					{
						stopwatch.Reset();
						Thread.Sleep(6);
						continue;
					}

					bool didWork = false;

					//lock (spectrum)
					{
						var spectrumResolver = spectrum.windWaves.SpectrumResolver;

						if(spectrumResolver == null)
							continue;

						int recentResultIndex = spectrum.recentResultIndex;
						int slotIndexPlus2 = (recentResultIndex + 2) % spectrum.resultsTiming.Length;
						int slotIndexPlus1 = (recentResultIndex + 1) % spectrum.resultsTiming.Length;
						float recentSlotTime = spectrum.resultsTiming[recentResultIndex];
						float slotPlus2Time = spectrum.resultsTiming[slotIndexPlus2];
						float slotPlus1Time = spectrum.resultsTiming[slotIndexPlus1];
						float currentTime = spectrumResolver.LastFrameTime;
						
						if(slotPlus2Time <= currentTime || slotPlus1Time > currentTime)
						{
							float computedSnapshotTime = Mathf.Max(recentSlotTime, currentTime) + fftTimeStep;

							if (slotPlus1Time > currentTime)
								computedSnapshotTime = currentTime + fftTimeStep;

							fftTask.Compute(spectrum, computedSnapshotTime, slotIndexPlus1);

							spectrum.resultsTiming[slotIndexPlus1] = computedSnapshotTime;
							spectrum.recentResultIndex = slotIndexPlus1;
							
							didWork = true;
                        }
					}

					if(!didWork)
					{
						stopwatch.Reset();
						Thread.Sleep(3);
					}
				}
			}
			catch(System.Exception e)
			{
				threadException = e;
			}
		}
	}
}
