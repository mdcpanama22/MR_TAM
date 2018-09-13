using System;
using UnityEngine;

namespace PlayWay.Water
{
	[Obsolete("It's now built into Water component. Use Water.WindWaves property to access its features in your scripts.")]
	public sealed class WindWavesDeprecated : MonoBehaviour
	{
		public Transform windDirectionPointer;
		public int resolution = 256;
		public bool highPrecision = true;
		public float cpuWaveThreshold = 0.008f;
		public int cpuMaxWaves = 2500;
		public int cpuFFTPrecisionBoost = 1;
		public WindWaves copyFrom;
		public WaveSpectrumRenderMode renderMode;
		public WindWaves.WindWavesEvent windDirectionChanged;
		public WindWaves.WindWavesEvent resolutionChanged;
		public WavesRendererFFT waterWavesFFT;
		public WavesRendererGerstner waterWavesGerstner;
		public DynamicSmoothness dynamicSmoothness;
		public float mipBias = 0.0f;
	}
}
