using UnityEngine;
using UnityEngine.Serialization;

namespace PlayWay.Water
{
	public class ShipBowWavesEmitter : MonoBehaviour
	{
		[FormerlySerializedAs("water")]
		[SerializeField] private WaveParticlesSystemGPU gpuParticleSystem;
		[SerializeField] private ParticleSystem unityParticleSystem;

		[Range(0.02f, 0.98f)]
		[SerializeField] private float waveSpeed = 0.5f;

		[SerializeField] private float amplitude = 0.5f;
		[SerializeField] private float wavelength = 6.0f;
		[SerializeField] private float lifetime = 50.0f;
		[SerializeField] private float foam = 1.0f;
		[SerializeField] private float maxShipSpeed = 16.5f;
		[Range(0.0f, 1.0f)] [SerializeField] private float trailCalming = 1.0f;
		[Range(0.0f, 8.0f)] [SerializeField] private float trailFoam = 1.0f;

		[Header("Advanced")]

		[Tooltip("Use for submarines. Allows emission to be moved to exposed ship parts during submerge process and completely disabled after complete submarge.")]
		[SerializeField] private bool advancedEmissionPositioning;

		[Tooltip("Required if 'advancedEmissionPositioning' is enabled. Allows emitter to determine an emission point on that collider.")]
		[SerializeField] private Collider shipCollider;

		[SerializeField] private float advancedEmissionOffset = 2.0f;
		[SerializeField] private int minTextureIndex = 0;
		[SerializeField] private int maxTextureIndex = 4;

		[Range(0.1f, 1.0f)]
		[SerializeField] private float emissionSpacing = 0.45f;

		private Vector2 previousFrameBowPosition;
		private float totalBowDeltaMagnitude;
		private float lastBowEmitTime;
		private float angleSin, angleCos;
		private float space;
		private bool useBuiltinParticleSystem;
		private Water waterComponent;

		private void Start()
		{
			if (gpuParticleSystem == null)
				gpuParticleSystem = FindObjectOfType<WaveParticlesSystemGPU>();

			useBuiltinParticleSystem = unityParticleSystem == null;

			waterComponent = gpuParticleSystem.GetComponent<Water>();
			OnValidate();

			Vector2 bowPosition = GetVector2(transform.position);
			Vector2 bowPositionWithOffset = bowPosition + waterComponent.SurfaceOffset;
			previousFrameBowPosition = bowPositionWithOffset;
		}

		private void OnValidate()
		{
			space = wavelength * emissionSpacing;

			float angle = Mathf.Acos(waveSpeed);
			angleSin = Mathf.Sin(angle);
			angleCos = Mathf.Cos(angle);
		}

		private void LateUpdate()
		{
			Vector2 bowPosition = GetVector2(transform.position);
			Vector2 bowPositionWithOffset = bowPosition + waterComponent.SurfaceOffset;
			Vector2 bowDelta = bowPositionWithOffset - previousFrameBowPosition;
			Vector2 bowForward = GetVector2(transform.forward).normalized;
				
			previousFrameBowPosition = bowPositionWithOffset;

			float bowDeltaMagnitudeSq = bowDelta.x * bowForward.x + bowDelta.y * bowForward.y;

			if(bowDeltaMagnitudeSq < 0.0f)
				return;
			
			float bowDeltaMagnitude = bowDeltaMagnitudeSq;
			totalBowDeltaMagnitude += bowDeltaMagnitude;
			
			if (totalBowDeltaMagnitude >= space)
			{
				float time = Time.time;
				float timeSpan = time - lastBowEmitTime;
				lastBowEmitTime = time;

				float shipSpeed = totalBowDeltaMagnitude/timeSpan;

				if (shipSpeed >= maxShipSpeed)
					shipSpeed = maxShipSpeed;

				float waveSpeed = this.waveSpeed*shipSpeed;

				Vector2 rightWaveDirection = new Vector2(
					bowForward.x*angleCos - bowForward.y*angleSin,
					bowForward.x*angleSin + bowForward.y*angleCos
					);

				Vector2 leftWaveDirection = new Vector2(
					bowForward.x*angleCos + bowForward.y*angleSin,
					bowForward.y*angleCos - bowForward.x*angleSin
					);

				do
				{
					totalBowDeltaMagnitude -= space;

					if (advancedEmissionPositioning)
					{
						float waterElevation = waterComponent.transform.position.y + waterComponent.GetHeightAt(bowPosition.x, bowPosition.y, 0.0f, 1.0f, Time.time);
						RaycastHit hitInfo;

						if (!shipCollider.Raycast(
								new Ray(new Vector3(bowPosition.x, waterElevation, bowPosition.y), new Vector3(-bowForward.x, 0.0f, -bowForward.y)),
								out hitInfo, 100.0f))
							return;
						
						bowPosition = GetVector2(hitInfo.point) + bowForward * advancedEmissionOffset;
					}

					Vector2 displacement = waterComponent.GetHorizontalDisplacementAt(bowPosition.x, bowPosition.y, 0.0f, 1.0f, Time.time);
					bowPosition.x -= displacement.x;
					bowPosition.y -= displacement.y;

					if (useBuiltinParticleSystem)
					{
						gpuParticleSystem.EmitParticle(new WaveParticlesSystemGPU.ParticleData()
						{
							position = bowPosition,
							direction = leftWaveDirection*waveSpeed,
							amplitude = amplitude,
							wavelength = wavelength,
							initialLifetime = lifetime,
							lifetime = lifetime,
							foam = foam,
							uvOffsetPack =
								Random.Range(0, gpuParticleSystem.FoamAtlasHeight)/(float) gpuParticleSystem.FoamAtlasHeight*16 + Random.Range(minTextureIndex, maxTextureIndex)/(float) gpuParticleSystem.FoamAtlasWidth,
							trailCalming = trailCalming,
							trailFoam = trailFoam
						});

						gpuParticleSystem.EmitParticle(new WaveParticlesSystemGPU.ParticleData()
						{
							position = bowPosition,
							direction = rightWaveDirection*waveSpeed,
							amplitude = amplitude,
							wavelength = wavelength,
							initialLifetime = lifetime,
							lifetime = lifetime,
							foam = foam,
							uvOffsetPack =
								Random.Range(0, gpuParticleSystem.FoamAtlasHeight)/(float) gpuParticleSystem.FoamAtlasHeight*16 + Random.Range(minTextureIndex, maxTextureIndex)/(float) gpuParticleSystem.FoamAtlasWidth,
							trailCalming = trailCalming,
							trailFoam = trailFoam
						});
					}
					else
					{
						var emitParams = new ParticleSystem.EmitParams();
						emitParams.position = new Vector3(bowPosition.x, waterComponent.transform.position.y, bowPosition.y);
						emitParams.velocity = new Vector3(leftWaveDirection.x, 0.0f, leftWaveDirection.y)*waveSpeed;
						unityParticleSystem.Emit(emitParams, 1);
						
						emitParams.velocity = new Vector3(rightWaveDirection.x, 0.0f, rightWaveDirection.y) * waveSpeed;
						unityParticleSystem.Emit(emitParams, 1);
					}
				} while (totalBowDeltaMagnitude >= space);
			}
		}

		private static Vector2 GetVector2(Vector3 vector3)
		{
			return new Vector2(vector3.x, vector3.z);
		}
	}
}
