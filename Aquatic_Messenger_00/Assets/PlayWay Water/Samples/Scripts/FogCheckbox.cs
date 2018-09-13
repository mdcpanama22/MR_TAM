using UnityEngine;
using UnityEngine.UI;

namespace PlayWay.WaterSamples
{
	public sealed class FogCheckbox : MonoBehaviour
	{
		private void Awake()
		{
			var toggle = GetComponent<Toggle>();
			toggle.onValueChanged.AddListener(OnValueChanged);
		}

		private static void OnValueChanged(bool value)
		{
			RenderSettings.fog = value;
		}
	}
}
