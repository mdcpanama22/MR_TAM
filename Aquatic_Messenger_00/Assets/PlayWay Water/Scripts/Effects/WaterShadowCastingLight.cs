using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water
{
	public sealed class WaterShadowCastingLight : MonoBehaviour
	{
		private CommandBuffer commandBuffer1;

		private void Start()
		{
			int shadowmapId = Shader.PropertyToID("_WaterShadowmap");

			commandBuffer1 = new CommandBuffer();
			commandBuffer1.name = "Water: Copy Shadowmap";
			commandBuffer1.GetTemporaryRT(shadowmapId, Screen.width, Screen.height, 32, FilterMode.Point,
				RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			commandBuffer1.Blit(BuiltinRenderTextureType.CurrentActive, shadowmapId);

			var light = GetComponent<Light>();
			light.AddCommandBuffer(LightEvent.AfterScreenspaceMask, commandBuffer1);
		}
	}
}
