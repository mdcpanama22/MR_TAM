using UnityEngine;

namespace PlayWay.Water
{
	public class PlayWayWaterShip : MonoBehaviour
	{
		[SerializeField] private ShipBowWavesEmitter bowWavesEmitter;
		[SerializeField] private ParticleSystem[] sprayEmitters;
		[SerializeField] private float maxVelocity = 2.0f;

		private Vector3 lastPosition;
		private float velocity;
		private ParticleSystemData[] initialData;

		private void Start()
		{
			initialData = new ParticleSystemData[sprayEmitters.Length];

			for (int i = initialData.Length - 1; i >= 0; --i)
			{
				initialData[i] = new ParticleSystemData()
				{
					rateOverTimeMultiplier = sprayEmitters[i].emission.rateOverTimeMultiplier,
					startSpeedMultiplier = sprayEmitters[i].main.startSpeedMultiplier
				};
			}
		}

		private void FixedUpdate()
		{
			Vector3 position = transform.position;
			Vector3 delta = position - lastPosition;
			lastPosition = position;

			velocity = delta.magnitude / Time.fixedDeltaTime;

			float effectsIntensity = 1.0f - Mathf.Exp(-velocity / maxVelocity);

			for (int i = sprayEmitters.Length - 1; i >= 0; --i)
			{
				var initialData = this.initialData[i];

				var emission = sprayEmitters[i].emission;
				emission.rateOverTimeMultiplier = initialData.rateOverTimeMultiplier * effectsIntensity;

				var main = sprayEmitters[i].main;
				main.startSpeedMultiplier = initialData.startSpeedMultiplier * effectsIntensity;
				//main.startColor = main.startColor.color.MaskA(effectsIntensity);
			}
		}

		public class ParticleSystemData
		{
			public float rateOverTimeMultiplier;
			public float startSpeedMultiplier;
		}
	}
}
