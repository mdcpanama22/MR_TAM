using UnityEngine;

namespace PlayWay.Water
{
	[System.Serializable]
	public sealed class BlurSSS : Blur
	{
		[SerializeField]
		private bool initializedDefaults;

		private static int maxDistanceHash = -1;
		private static int absorptionColorPerPixelHash;

		public void Apply(RenderTexture source, RenderTexture target, Color absorptionColor, float worldSpaceSize, float lightFractionToIgnore)
		{
			//if(SystemInfo.supportsComputeShaders)
			//	ApplyComputeShader(target, absorptionColor, worldSpaceSize, lightFractionToIgnore);
			//else
				ApplyPixelShader(source, target, absorptionColor, worldSpaceSize, lightFractionToIgnore);
		}

		private void ApplyComputeShader(RenderTexture target, Color absorptionColor, float worldSpaceSize, float lightFractionToIgnore)
		{
			if (shaderWeights == null)
				shaderWeights = new ComputeBuffer(4, 16);

			float pixelSize = -worldSpaceSize/target.width;

			float maxComponent = Mathf.Max(Mathf.Max(absorptionColor.r, absorptionColor.g), absorptionColor.b);
			float distanceInPixelsFloat = Mathf.Log(lightFractionToIgnore)/(maxComponent*pixelSize);
			int distanceInPixels = Mathf.Clamp(Mathf.CeilToInt(distanceInPixelsFloat), 8, target.width);

			shaderWeights.SetData(new[]
			{
				new Color(0.324f, 0.324f, 0.324f, 1.0f),
				new Color(Mathf.Exp(absorptionColor.r * pixelSize), Mathf.Exp(absorptionColor.g * pixelSize), Mathf.Exp(absorptionColor.b * pixelSize), 1.0f),
				new Color(Mathf.Exp(absorptionColor.r * pixelSize * 2.0f), Mathf.Exp(absorptionColor.g * pixelSize * 2.0f), Mathf.Exp(absorptionColor.b * pixelSize * 2.0f), 1.0f),
				new Color(Mathf.Exp(absorptionColor.r * pixelSize * 3.0f), Mathf.Exp(absorptionColor.g * pixelSize * 3.0f), Mathf.Exp(absorptionColor.b * pixelSize * 3.0f), 1.0f)
			});

			blurComputeShader.SetFloats("_AbsorptionColorPerPixel", absorptionColor.r*pixelSize, absorptionColor.g*pixelSize, absorptionColor.b*pixelSize, 1.0f);
			blurComputeShader.SetInt("_FilterSize", distanceInPixels);
			blurComputeShader.SetFloat("_DensityWeight", -pixelSize);
			ApplyComputeShader(target);
		}
		
		private void ApplyPixelShader(RenderTexture source, RenderTexture target, Color absorptionColor, float worldSpaceSize, float lightFractionToIgnore)
		{
			if(maxDistanceHash == -1)
				InitializeStaticFields();

			Color absorptionColorPerPixel = absorptionColor*(-2.5f*worldSpaceSize);

			// it's multiplied by -1, so min is actually max
			float minAbsorption = absorptionColorPerPixel.r > absorptionColorPerPixel.g ? absorptionColorPerPixel.r : absorptionColorPerPixel.g;

			if(absorptionColorPerPixel.b > minAbsorption)
				minAbsorption = absorptionColorPerPixel.b;

			float maxDistance = Mathf.Log(lightFractionToIgnore) / minAbsorption;
			
			var originalFilterMode = source.filterMode;
			source.filterMode = FilterMode.Bilinear;

			var blurMaterial = BlurMaterial;
			blurMaterial.SetColor(absorptionColorPerPixelHash, absorptionColorPerPixel);
			blurMaterial.SetFloat(maxDistanceHash, maxDistance);
			Graphics.Blit(source, target, blurMaterial, 0);

			source.filterMode = originalFilterMode;
		}

		public override void Validate(string shaderName, string computeShaderName = null, int kernelIndex = 0)
		{
			base.Validate(shaderName, computeShaderName, kernelIndex);

			if(!initializedDefaults)
			{
				Iterations = 5;
				initializedDefaults = true;
			}
		}

		private static void InitializeStaticFields()
		{
			maxDistanceHash = Shader.PropertyToID("_MaxDistance");
			absorptionColorPerPixelHash = Shader.PropertyToID("_AbsorptionColorPerPixel");
		}
	}
}
