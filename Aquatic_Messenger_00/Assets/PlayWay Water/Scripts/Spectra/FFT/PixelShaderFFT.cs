using UnityEngine;

namespace PlayWay.Water
{
	/// <summary>
	///     Computes FFT on shader model 2.0 pixel shaders. The only considerable requirement is the support for at least half
	///     precision fp render textures.
	/// </summary>
	public sealed class PixelShaderFFT : GpuFFT
	{
		private TemporaryRenderTexture rt1;
		private TemporaryRenderTexture rt2;

		private readonly Material material;
		private readonly int butterflyTexProperty;
		private readonly int butterflyPassProperty;

		public PixelShaderFFT(Shader fftShader, int resolution, bool highPrecision, bool twoChannels) : base(resolution, highPrecision, twoChannels, false)
		{
			material = new Material(fftShader) {hideFlags = HideFlags.DontSave};

			butterflyTexProperty = Shader.PropertyToID("_ButterflyTex");
			butterflyPassProperty = Shader.PropertyToID("_ButterflyPass");
		}

		public override void Dispose()
		{
			base.Dispose();

			if(material == null)
				Object.Destroy(material);
		}

		public override void SetupMaterials()
		{
			material.SetTexture(butterflyTexProperty, Butterfly);
		}

		public override void ComputeFFT(Texture tex, RenderTexture target)
		{
			using(rt1 = renderTexturesSet.GetTemporary())
			using(rt2 = renderTexturesSet.GetTemporary())
			{
				ComputeFFT(tex, null, twoChannels ? 2 : 0);
				ComputeFFT(rt1, target, twoChannels ? 3 : 1);
			}
		}

		private void ComputeFFT(Texture tex, RenderTexture target, int passIndex)
		{
			material.SetFloat(butterflyPassProperty, 0.5f / numButterfliesPow2);
			Graphics.Blit(tex, rt2, material, passIndex);

			SwapRT();

			for(int i = 1; i < numButterflies; ++i)
			{
				if(target != null && i == numButterflies - 1)
				{
					material.SetFloat(butterflyPassProperty, (i + 0.5f) / numButterfliesPow2);
					Graphics.Blit(rt1, target, material, passIndex == 1 ? 4 : 5);
				}
				else
				{
					material.SetFloat(butterflyPassProperty, (i + 0.5f) / numButterfliesPow2);
					Graphics.Blit(rt1, rt2, material, passIndex);
				}

				SwapRT();
			}
		}

		private void SwapRT()
		{
			var t = rt1;
			rt1 = rt2;
			rt2 = t;
		}
	}
}
