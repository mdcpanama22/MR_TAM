using System;
using UnityEngine;

namespace PlayWay.Water
{
	[Serializable]
	public class ProfilesManager
	{
		[SerializeField]
		private WaterProfile initialProfile;

		[SerializeField]
		private Water.WaterEvent changed;

		private Water water;
		private bool profilesDirty;
		private WaterProfile initialProfileCopy;

		internal void Start(Water water)
		{
			this.water = water;

			if(changed == null)
				changed = new Water.WaterEvent();

#if UNITY_EDITOR
			if(initialProfile == null)
				initialProfile = WaterPackageUtilities.FindDefaultAsset<WaterProfile>("\"Sea - 6. Strong Breeze\" t:WaterProfile", "t:WaterProfile");
#endif

			if(Profiles == null)
			{
				if(initialProfile != null)
					SetProfiles(new Water.WeightedProfile(initialProfile, 1.0f));
				else
					Profiles = new Water.WeightedProfile[0];
			}

			WaterQualitySettings.Instance.Changed -= OnQualitySettingsChanged;
			WaterQualitySettings.Instance.Changed += OnQualitySettingsChanged;
		}

		internal void Enable()
		{
			profilesDirty = true;
		}

		internal void Disable()
		{

		}

		internal void Destroy()
		{
			WaterQualitySettings.Instance.Changed -= OnQualitySettingsChanged;
		}

		/// <summary>
		/// Currently set water profiles with their associated weights.
		/// </summary>
		public Water.WeightedProfile[] Profiles { get; private set; }

		public Water.WaterEvent Changed
		{
			get { return changed; }
		}

		/// <summary>
		/// Caches profiles for later use to avoid hiccups.
		/// </summary>
		/// <param name="profiles"></param>
		public void CacheProfiles(params WaterProfile[] profiles)
		{
			var windWaves = water.WindWaves;

			if(windWaves != null)
			{
				for(int i = 0; i < profiles.Length; ++i)
					windWaves.SpectrumResolver.CacheSpectrum(profiles[i].Spectrum);
			}
		}

		/// <summary>
		/// Lets you quickly evaluate a choosen property from the water profiles by using a lambda expression.
		/// </summary>
		/// <param name="func"></param>
		/// <returns></returns>
		public float EvaluateProfilesParameter(Func<WaterProfile, float> func)
		{
			float sum = 0.0f;

			var profiles = Profiles;

			for(int i = profiles.Length - 1; i >= 0; --i)
				sum += func(profiles[i].Profile) * profiles[i].Weight;

			return sum;
		}

		/// <summary>
		/// Sets water profiles with custom weights.
		/// </summary>
		/// <param name="profiles"></param>
		public void SetProfiles(params Water.WeightedProfile[] profiles)
		{
			CheckProfiles(profiles);

			Profiles = profiles;
			profilesDirty = true;
		}

		/// <summary>
		/// Instantly populates all water properties from the used profiles. Normally it's delayed and done on each update.
		/// </summary>
		public void ValidateProfiles()
		{
			if(profilesDirty)
			{
				profilesDirty = false;
				changed.Invoke(water);
			}
		}

		internal void Update()
		{
			ValidateProfiles();
		}

		internal void Validate()
		{
			if(Profiles != null && Profiles.Length != 0 && (initialProfileCopy == initialProfile || initialProfileCopy == null))
			{
				initialProfileCopy = initialProfile;
				profilesDirty = true;
			}
			else if(initialProfile != null)
			{
				initialProfileCopy = initialProfile;
				Profiles = new[] { new Water.WeightedProfile(initialProfile, 1.0f) };
				profilesDirty = true;
			}
		}

		private void OnQualitySettingsChanged()
		{
			profilesDirty = true;
		}

		/// <summary>
		/// Ensures that profiles are fine to use.
		/// </summary>
		/// <param name="profiles"></param>
		private static void CheckProfiles(Water.WeightedProfile[] profiles)
		{
			if(profiles.Length == 0)
				throw new ArgumentException("Water has to use at least one profile.");

			float tileSize = profiles[0].Profile.TileSize;

			for(int i = 1; i < profiles.Length; ++i)
			{
				if(profiles[i].Profile.TileSize != tileSize)
				{
					Debug.LogError("TileSize varies between used water profiles. It is the only parameter that you should keep equal on all profiles used at a time.");
					break;
				}
			}
		}
	}
}
