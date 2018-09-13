using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	/// <summary>
	/// Renders water just after all opaque objects. Works fine with fog effects etc.
	/// </summary>
	[ExecuteInEditMode]
	public sealed class WaterCameraIME : MonoBehaviour
	{
		private WaterCamera waterCamera;

		private void Awake()
		{
			waterCamera = GetComponent<WaterCamera>();
		}

		[ImageEffectOpaque]
		private void OnRenderImage(RenderTexture source, RenderTexture destination)
		{
			if(waterCamera == null)
			{
				Graphics.Blit(source, destination);
				Destroy(this);
				return;
			}

			waterCamera.OnRenderImageCallback(source, destination);
        }
	}

	public enum WaterRenderMode
	{
		DefaultQueue,
		ImageEffectForward,
		ImageEffectDeferred
	}
}
