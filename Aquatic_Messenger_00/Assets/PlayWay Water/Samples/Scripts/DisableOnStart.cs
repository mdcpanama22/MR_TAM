using UnityEngine;

namespace PlayWay.WaterSamples
{
	public class DisableOnStart : MonoBehaviour
	{
		[SerializeField]
		private float delay = 0.0f;

		private void Update()
		{
			if(Time.time >= delay)
				gameObject.SetActive(false);
		}
	}
}
