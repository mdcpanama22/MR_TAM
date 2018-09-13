using System;
using UnityEngine;

namespace PlayWay.Water
{
	[Obsolete("It's now built into Water component. Use Water.Foam property to access its features in your scripts.")]
	public sealed class WaterFoamDeprecated : MonoBehaviour
	{
		public float supersampling = 1.0f;
	}
}
