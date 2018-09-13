using UnityEngine;
using UnityEngine.UI;
using PlayWay.Water;

namespace PlayWay.WaterSamples
{
	public sealed class PresetDropdown : MonoBehaviour
	{
		[SerializeField]
		private Water.Water water;

		[SerializeField]
		private WaterProfile[] profiles;

#if !UNITY_5_0 && !UNITY_5_1
		[SerializeField]
		private Dropdown dropdown;
#endif

		[SerializeField]
		private Slider progressSlider;

		private WaterProfile sourceProfile;
		private WaterProfile targetProfile;
		private float changeTime = float.NaN;

		private void Start()
		{
#if !UNITY_5_0 && !UNITY_5_1
			dropdown.onValueChanged.AddListener(OnValueChanged);
#endif

			if(water.ProfilesManager.Profiles == null)
			{
				enabled = false;
				return;
			}

			targetProfile = water.ProfilesManager.Profiles[0].Profile;
		}

		public void SkipPresetTransition()
		{
			changeTime = -100.0f;
		}

		private void Update()
		{
			if(!float.IsNaN(changeTime))
			{
				float p = Mathf.Clamp01((Time.time - changeTime) / 30.0f);

				water.ProfilesManager.SetProfiles(
					new Water.Water.WeightedProfile(sourceProfile, 1.0f - p),
					new Water.Water.WeightedProfile(targetProfile, p)
				);

				progressSlider.value = p;

				if(p == 1.0f)
				{
					changeTime = float.NaN;
					progressSlider.transform.parent.gameObject.SetActive(false);

#if !UNITY_5_0 && !UNITY_5_1
					dropdown.interactable = true;
#endif
				}
			}
		}

		private void OnValueChanged(int index)
		{
			sourceProfile = targetProfile;
			targetProfile = profiles[index];
			changeTime = Time.time;

			progressSlider.transform.parent.gameObject.SetActive(true);

#if !UNITY_5_0 && !UNITY_5_1
			dropdown.interactable = false;
#endif
		}
	}
}
