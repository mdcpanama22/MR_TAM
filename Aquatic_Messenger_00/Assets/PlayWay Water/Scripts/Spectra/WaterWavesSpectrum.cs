using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Base class for oceanic omnidirectional spectrum generators.
	/// </summary>
	public abstract class WaterWavesSpectrum
	{
		protected float tileSize;
		protected float gravity;
		protected float windSpeed;
		protected float amplitude;

		protected WaterWavesSpectrum(float tileSize, float gravity, float windSpeed, float amplitude)
		{
			this.tileSize = tileSize;
			this.gravity = gravity;
			this.windSpeed = windSpeed;
			this.amplitude = amplitude;
		}

		public float TileSize
		{
			get { return tileSize * WaterQualitySettings.Instance.TileSizeScale; }
		}

		public float Gravity
		{
			get { return gravity; }
		}
		
		public abstract void ComputeSpectrum(Vector3[] spectrum, float tileSizeMultiplier, int maxResolution, System.Random random);
	}
}
