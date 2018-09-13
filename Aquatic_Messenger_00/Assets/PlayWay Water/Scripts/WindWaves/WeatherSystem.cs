using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	/// Describes some external weather system (like a storm) that may travel around the current location or even cross it.
	/// - Forward vector of the transform is the weather system wind direction.
	/// - Position of the transform is the weather system position.
	/// </summary>
	public class WeatherSystem : MonoBehaviour
	{
		[SerializeField]
		private Water water;

		[SerializeField]
		private WaterProfile profile;

		[Tooltip("Describes how big the weather system is. Common values range from 10000 to 150000, assuming that the scene units are used as meters.")]
		[SerializeField]
		private float radius = 10000;

		[Range(0.0f, 1.0f)]
		[SerializeField]
		private float weight = 1.0f;
		
		private WaterWavesSpectrumData spectrumData;
		private Vector2 lastOffset;
		private Vector2 lastWindDirection;
		private float lastRadius;
		private float lastWeight;

		private void Start()
		{
			spectrumData = new WaterWavesSpectrumData(water, water.WindWaves, profile.Spectrum);
			LateUpdate();
			water.WindWaves.SpectrumResolver.AddSpectrum(spectrumData);
		}

		private void OnEnable()
		{
			if(spectrumData != null && !water.WindWaves.SpectrumResolver.ContainsSpectrum(spectrumData))
				water.WindWaves.SpectrumResolver.AddSpectrum(spectrumData);
		}

		private void OnDisable()
		{
			water.WindWaves.SpectrumResolver.RemoveSpectrum(spectrumData);
		}

		private void LateUpdate()
		{
			Vector3 offset3d = water.transform.InverseTransformPoint(transform.position);
			Vector2 offset = new Vector2(offset3d.x, offset3d.z);

			Vector3 windDirection3d = transform.forward;
			Vector2 windDirection = new Vector2(windDirection3d.x, windDirection3d.z).normalized;

			if (windDirection != lastWindDirection || offset != lastOffset || radius != lastRadius || weight != lastWeight)
			{
				spectrumData.WindDirection = lastWindDirection = windDirection;
				spectrumData.WeatherSystemOffset = lastOffset = offset;
				spectrumData.WeatherSystemRadius = lastRadius = radius;
				spectrumData.Weight = lastWeight = weight;
				water.WindWaves.SpectrumResolver.SetDirectionalSpectrumDirty();
			}
		}
	}
}
