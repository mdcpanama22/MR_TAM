using System.Diagnostics;
using UnityEngine;

namespace PlayWay.WaterSamples
{
	/// <summary>
	/// Use this test script on your own responsibility. It may burn CPUs without proper cooling.
	/// </summary>
	public class StressTest : MonoBehaviour
	{
		[SerializeField]
		private float msPerFrame = 1.0f;

		private void Update()
		{
			Stopwatch stopwatch = new Stopwatch();
			stopwatch.Start();
			float t = 84.31356f;

			while(stopwatch.ElapsedMilliseconds < msPerFrame)
			{
				for(int i = 0; i < 100; ++i)
				{
					t += 0.01f;
					t -= 0.01f;
					t *= 2.0f;
					t *= 0.5f;
				}
			}
		}
	}
}
