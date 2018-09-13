using UnityEngine;

namespace PlayWay.Water
{
	public class ResolutionAttribute : PropertyAttribute
	{
		private readonly int recommendedResolution;
		private readonly int[] resolutions;

		public ResolutionAttribute(int recommendedResolution, params int[] resolutions)
		{
			this.recommendedResolution = recommendedResolution;
			this.resolutions = resolutions;
		}

		public int RecommendedResolution
		{
			get { return recommendedResolution; }
		}

		public int[] Resolutions
		{
			get { return resolutions; }
		}
	}
}
