using UnityEngine;
using System.Collections;

//[AddComponentMenu("Scripts/AI/System/Current")]
public class CurrentHandler : MonoBehaviour {

	public static CurrentHandler singleton;
	private float wavePosition;
	public float frequency;
	public float wavelength;
	public float amplitudeX;
	public float amplitudeY;

	void Awake () {
		if (singleton == null) {
			singleton = this;
		}
		wavePosition = 0f;
	}

	void FixedUpdate () {
		wavePosition += frequency * Time.fixedDeltaTime;
		if (wavePosition > wavelength) {
			wavePosition -= wavelength;
		}
	}

	public Vector3 GetCurrentAtPosition (Vector3 position) {
		float modifiedAngle = ((wavePosition + position.z) / wavelength) * 2f * Mathf.PI;
		return new Vector3(Mathf.Cos(modifiedAngle) * amplitudeX, Mathf.Sin(modifiedAngle) * amplitudeY, 0f);
	}

}
