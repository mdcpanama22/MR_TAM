using UnityEngine;
using UnityEngine.Rendering;

namespace PlayWay.Water.Internal
{
	public static class GraphicsUtilities
	{
		private static CommandBuffer commandBuffer;

		public static void Blit(Texture source, RenderTexture target, Material material, int shaderPass, MaterialPropertyBlock properties)
		{
			if(commandBuffer == null)
				CreateCommandBuffer();

			GL.PushMatrix();
			GL.modelview = Matrix4x4.identity;
			GL.LoadProjectionMatrix(Matrix4x4.identity);
			
			material.mainTexture = source;
			commandBuffer.SetRenderTarget(target);
			commandBuffer.DrawMesh(Quads.BipolarXY, Matrix4x4.identity, material, 0, shaderPass, properties);
			
			Graphics.ExecuteCommandBuffer(commandBuffer);

			// setting target permanently seems to be the default behaviour of regular Graphics.Blit and without it some problems arise
			RenderTexture.active = target;
			GL.PopMatrix();

			commandBuffer.Clear();
		}

		private static void CreateCommandBuffer()
		{
			commandBuffer = new CommandBuffer { name = "PlayWay Water Custom Blit" };
		}
	}
}
