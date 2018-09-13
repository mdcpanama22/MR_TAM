using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	/// Emits water spray particles.
	/// </summary>
	public sealed class WaterSprayEmitter : MonoBehaviour
	{
		[SerializeField]
		private Spray water;

		[SerializeField]
		private float emissionRate = 5.0f;

		[SerializeField]
		private float startIntensity = 1.0f;

		[SerializeField]
		private float startVelocity = 1.0f;

		[SerializeField]
		private float lifetime = 4.0f;

		private float totalTime;
		private float timeStep;
		private Spray.Particle[] particles;

		private void Start()
		{
			OnValidate();
			particles = new Spray.Particle[Mathf.Max(1, (int)emissionRate)];
        }

		public float StartVelocity
		{
			get { return startVelocity; }
			set { startVelocity = value; }
		}

		private void Update()
		{
			int particleIndex = 0;
			totalTime += Time.deltaTime;

			while(totalTime >= timeStep && particleIndex < particles.Length)
			{
				totalTime -= timeStep;

				particles[particleIndex].Lifetime = new Vector2(lifetime, lifetime);
				particles[particleIndex].MaxIntensity = startIntensity;
				particles[particleIndex].Position = transform.position + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f), Random.Range(-0.3f, 0.3f));
				particles[particleIndex].Velocity = transform.forward * startVelocity;
				particles[particleIndex++].Offset = Random.Range(0.0f, 10.0f);
			}

			if(particleIndex != 0)
				water.SpawnCustomParticles(particles, particleIndex);
		}

		private void OnValidate()
		{
			timeStep = 1.0f / emissionRate;
		}
	}
}
