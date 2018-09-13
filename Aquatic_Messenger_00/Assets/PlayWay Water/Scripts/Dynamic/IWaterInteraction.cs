using UnityEngine;

namespace PlayWay.Water
{
	public interface IWaterInteraction
	{
		void OnInteractionPreRender(Camera camera, float waterVerticalOffset, int layerMask);
		void OnInteractionPostRender(DynamicWaterCameraData overlays);
		void RenderInteractionDirect();
	}
}
