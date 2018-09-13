﻿using UnityEngine;

namespace PlayWay.Water
{
	[System.Serializable]
	public struct NormalMapAnimation
	{
		[SerializeField]
		private float speed;

		[Tooltip("Angular deviation from the wind direction.")]
		[SerializeField]
		private float deviation;

		[SerializeField]
		[Range(0.0f, 4.0f)]
		private float intensity;

		[SerializeField]
		private Vector2 tiling;

		public NormalMapAnimation(float speed, float deviation, float intensity, Vector2 tiling)
		{
			this.speed = speed;
			this.deviation = deviation;
			this.intensity = intensity;
			this.tiling = tiling;
		}

		public float Speed
		{
			get { return speed; }
		}

		public float Deviation
		{
			get { return deviation; }
		}

		public float Intensity
		{
			get { return intensity; }
		}

		public Vector2 Tiling
		{
			get { return tiling; }
		}

		static public NormalMapAnimation operator *(NormalMapAnimation a, float w)
		{
			return new NormalMapAnimation(a.speed * w, a.deviation * w, a.intensity * w, a.tiling * w);
		}

		static public NormalMapAnimation operator +(NormalMapAnimation a, NormalMapAnimation b)
		{
			return new NormalMapAnimation(a.speed + b.speed, a.deviation + b.deviation, a.intensity + b.intensity, a.tiling + b.tiling);
		}
	}
}
