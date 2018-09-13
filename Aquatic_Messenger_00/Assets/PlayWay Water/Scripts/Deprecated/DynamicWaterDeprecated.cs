using System;
using UnityEngine;

namespace PlayWay.Water
{
	[Obsolete("It's now built into Water component. Please use Water.DynamicWater property to access its features in your scripts.")]
	public sealed class DynamicWaterDeprecated : MonoBehaviour
	{
		public int antialiasing = 1;
		public LayerMask interactionMask = int.MaxValue;
	}
}
