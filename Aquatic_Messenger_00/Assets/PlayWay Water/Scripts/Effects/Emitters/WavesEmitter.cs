using UnityEngine;
using UnityEngine.Serialization;

namespace PlayWay.Water
{
	/// <summary>
	/// Emits waves at a steady rate. Use Power property to adjust its intensity.
	/// </summary>
	public class WavesEmitter : MonoBehaviour
	{
		[SerializeField] private WaveParticlesSystemGPU water;

		[SerializeField] private float amplitude = 0.1f;
		[SerializeField] private float wavelength = 10.0f;
		[SerializeField] private float lifetime = 50.0f;
		[SerializeField] private float speed = 3.5f;
		[SerializeField] private float foam = 1.0f;
		[SerializeField] private float emissionArea = 1.0f;
		[SerializeField] private float emissionInterval = 0.15f;
		[Range(0.0f, 1.0f)] [SerializeField] private float trailCalming = 1.0f;
		[Range(0.0f, 8.0f)] [SerializeField] private float trailFoam = 1.0f;
		[Range(0.0f, 180.0f)] [SerializeField] private float emissionAngle = 0.0f;

		[Header("Advanced")]
		[FormerlySerializedAs("minTextureIndex")]
		[SerializeField] private int minTextureU = 4;
		[FormerlySerializedAs("maxTextureIndex")]
		[SerializeField] private int maxTextureU = 8;
		[SerializeField] private int minTextureV = 0;
		[SerializeField] private int maxTextureV = 4;

		[Range(0.0f, 1.0f)] [SerializeField] private float initialPower = 1.0f;

		private float power = -1;
		private float lastEmitTime;
		private float finalEmissionInterval;
		private Water waterComponent;

		private void Start()
		{
			if (water == null)
				water = FindObjectOfType<WaveParticlesSystemGPU>();

			waterComponent = water.GetComponent<Water>();
			Power = initialPower;
		}

		/// <summary>
		/// General intensity of the waves.
		/// </summary>
		public float Power
		{
			get { return power; }
			set
			{
				power = value > 0.0f ? value : 0.0f;
				finalEmissionInterval = emissionInterval / power;
				enabled = power != 0.0f;
			}
		}

		private void OnValidate()
		{
			finalEmissionInterval = emissionInterval / power;
		}

		private void LateUpdate()
		{
			float time = Time.time;

			if(time - lastEmitTime >= finalEmissionInterval)
			{
				lastEmitTime = time;

				Vector2 sternPosition = GetVector2(transform.position);
				Vector2 sternForward = GetVector2(transform.forward).normalized;
				Vector2 sternRight = GetVector2(transform.right).normalized;

				float emissionAngle = this.emissionAngle*Mathf.Deg2Rad;
				float angle = Random.Range(-emissionAngle, emissionAngle);
				float sin, cos;
				FastMath.SinCos2048(angle, out sin, out cos);

				Vector2 waveDirection = new Vector2(
						sternForward.x * cos - sternForward.y * sin,
						sternForward.x * sin + sternForward.y * cos
					);

				Vector2 emitPosition = sternPosition + sternRight * Random.Range(-emissionArea, emissionArea);

				Vector2 displacement = waterComponent.GetHorizontalDisplacementAt(emitPosition.x, emitPosition.y, 0.0f, 1.0f, Time.time);
				emitPosition.x -= displacement.x;
				emitPosition.y -= displacement.y;

				water.EmitParticle(new WaveParticlesSystemGPU.ParticleData()
				{
					position = emitPosition,
					direction = waveDirection * (speed * power),
					amplitude = amplitude * power,
					wavelength = wavelength,
					initialLifetime = lifetime * power,
					lifetime = lifetime * power,
					foam = foam * power,
					uvOffsetPack = Random.Range(0, water.FoamAtlasHeight) / (float)water.FoamAtlasHeight * 16 + Random.Range(minTextureU, maxTextureU) / (float)water.FoamAtlasWidth,
					trailCalming = trailCalming,
					trailFoam = trailFoam
				});
			}
		}

		private static Vector2 GetVector2(Vector3 vector3)
		{
			return new Vector2(vector3.x, vector3.z);
		}
	}
}
