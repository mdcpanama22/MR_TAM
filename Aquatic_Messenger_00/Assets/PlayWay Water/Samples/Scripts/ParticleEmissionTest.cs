using UnityEngine;

namespace PlayWay.Water
{
	public class ParticleEmissionTest : MonoBehaviour
	{
		private WaveParticlesSystemGPU particles;
		private Vector3 lastPos;

		private void Awake()
		{
			particles = GetComponent<WaveParticlesSystemGPU>();
		}

		private void Update()
		{
			if (Input.GetMouseButton(0))
			{
				Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
				Vector3 pos = ray.origin + ray.direction * (ray.origin.y / -ray.direction.y);

				if (Vector3.Distance(pos, lastPos) > 5.0f)
				{
					lastPos = pos;

					particles.EmitParticle(new WaveParticlesSystemGPU.ParticleData()
					{
						position = new Vector2(pos.x, pos.z),
						direction = new Vector2(0.0f, 4.0f),
						amplitude = 0.8f,
						wavelength = 9.0f,
						initialLifetime = 30.0f,
						lifetime = 30.0f
					});
				}
			}
		}
	}
}
