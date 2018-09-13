using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PlayWay.WaterSamples
{
	public sealed class FpsCounter : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
	{
		private Text label;

		private int frameCount;
		private float timeSum;
		private float minFps = 1000.0f;
		private float maxFps = 0.0f;
		private float avgFps = 60.0f;
		private bool showMoreData;

		private void Awake()
		{
			label = GetComponent<Text>();
		}

		private void Update()
		{
			++frameCount;
			timeSum += Time.unscaledDeltaTime;

			if(frameCount >= 10)
			{
				float fps = frameCount/timeSum;

				avgFps = avgFps * 0.95f + fps * 0.05f;

				if (Time.frameCount > 60)
				{
					if (minFps > fps)
						minFps = fps;

					if (maxFps < fps)
						maxFps = fps;
				}

				label.text = showMoreData ?
					string.Format("{0:0.0}\n\navg:{1:0.0}\nmin:{2:0.0}\nmax{3:0.0}", fps, avgFps, minFps, maxFps) :
					fps.ToString("0.0");

				frameCount = 0;
				timeSum = 0.0f;
			}
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			showMoreData = true;
		}

		public void OnPointerExit(PointerEventData eventData)
		{
			showMoreData = false;
		}
	}
}
