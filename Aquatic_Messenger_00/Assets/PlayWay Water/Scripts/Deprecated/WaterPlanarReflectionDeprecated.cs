using System;
using UnityEngine;

namespace PlayWay.Water
{
	[Obsolete("It's now built into Water component. Use Water.PlanarReflection property to access its features in your scripts.")]
	public sealed class WaterPlanarReflectionDeprecated : MonoBehaviour
	{
		public Camera reflectionCamera;
		public bool reflectSkybox = true;
		public int downsample = 2;
		public int retinaDownsample = 3;
		public LayerMask reflectionMask = int.MaxValue;
		public bool highQuality = true;
		public float clipPlaneOffset = 0.07f;
	}
}