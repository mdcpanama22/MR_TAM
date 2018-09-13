using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     It's a simple script that makes the transform follow the initial point on water below. It is faster than physical
	///     simulation and may be preferred for some small decal objects for performance reasons.
	/// </summary>
	public sealed class WaterFloat : MonoBehaviour
	{
		public enum DisplacementMode
		{
			Height,
			Displacement
		}

		[SerializeField]
		private DisplacementMode displacementMode = DisplacementMode.Displacement;

		[SerializeField]
		private float heightBonus = 0.0f;
		
		[Range(0.04f, 1.0f)]
		[SerializeField]
		private float precision = 0.2f;

		private Vector3 initialPosition;
		private Vector3 previousPosition;
		private WaterSample sample;

		[SerializeField] private Water water;

		private void Start()
		{
			initialPosition = transform.position;
			previousPosition = initialPosition;

			if (water == null)
				water = FindObjectOfType<Water>();

			sample = new WaterSample(water, (WaterSample.DisplacementMode) displacementMode, precision);
			sample.Start(transform.position);
		}

		private void OnDisable()
		{
			sample.Stop();
		}

		private void LateUpdate()
		{
			initialPosition += transform.position - previousPosition;

			Vector3 displaced = sample.GetAndReset(initialPosition.x, initialPosition.z,
				WaterSample.ComputationsMode.ForceCompletion);
			displaced.y += heightBonus;
			transform.position = displaced;

			previousPosition = displaced;
		}
	}
}