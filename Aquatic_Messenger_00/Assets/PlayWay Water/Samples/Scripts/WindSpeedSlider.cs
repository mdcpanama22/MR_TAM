using PlayWay.Water;
using UnityEngine;
using UnityEngine.UI;

namespace PlayWay.WaterSamples
{
	[RequireComponent(typeof(Slider))]
	public sealed class WindSpeedSlider : MonoBehaviour
	{
		[SerializeField]
		private Water.Water water;

		[SerializeField]
		private WaterProfile[] profiles;

		private Slider slider;
		private WaterProfile[] sortedProfiles;

		private void Awake()
		{
			slider = GetComponent<Slider>();
			slider.onValueChanged.AddListener(OnValueChanged);

			sortedProfiles = profiles;
			System.Array.Sort(sortedProfiles, (a, b) =>
			{
				if(a.WindSpeed < b.WindSpeed)
					return -1;
				else
					return a.WindSpeed == b.WindSpeed ? 0 : 1;
			});
        }

		private void Start()
		{
			water.ProfilesManager.CacheProfiles(sortedProfiles);
		}

		private void OnValueChanged(float newWindSpeed)
		{
			int profileIndex = 0;

			while(profileIndex < sortedProfiles.Length && sortedProfiles[profileIndex].WindSpeed < newWindSpeed) ++profileIndex;

			if(profileIndex == 0)
				water.ProfilesManager.SetProfiles(new Water.Water.WeightedProfile(sortedProfiles[profileIndex], 1.0f));
			else if(profileIndex == sortedProfiles.Length)
				water.ProfilesManager.SetProfiles(new Water.Water.WeightedProfile(sortedProfiles[profileIndex - 1], 1.0f));
			else
			{
				var profileA = sortedProfiles[profileIndex - 1];
				var profileB = sortedProfiles[profileIndex];

				float w = (newWindSpeed - profileA.WindSpeed) / (profileB.WindSpeed - profileA.WindSpeed);

				water.ProfilesManager.SetProfiles(
					new Water.Water.WeightedProfile(profileA, 1.0f - w),
					new Water.Water.WeightedProfile(profileB, w)
				);
			}
		}
	}
}
