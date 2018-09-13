using UnityEngine;

namespace PlayWay.Water
{
	[System.Serializable]
	public class Blur
	{
		[HideInInspector]
		[SerializeField]
		protected ComputeShader blurComputeShader;

		[HideInInspector]
		[SerializeField]
		private Shader blurShader;

		[HideInInspector]
		[SerializeField]
		private int computeShaderKernelIndex;

		[Range(0, 10)]
		[SerializeField]
		private int iterations = 1;

		[SerializeField]
		private float size = 0.005f;

		private Material blurMaterial;

		private int passIndex;
		protected ComputeBuffer shaderWeights;
		protected static int offsetHash;

		public Blur()
		{

		}

        public int Iterations
        {
            get { return iterations; }
            set
			{
				// preserve total blur size
				float totalSize = TotalSize;
				iterations = value;
				TotalSize = totalSize;
			}
        }

        public float Size
        {
            get { return size; }
            set { size = value; }
        }

		public float TotalSize
		{
			get { return size * iterations; }
			set { size = value / iterations; }
		}

        public Material BlurMaterial
		{
			get
			{
				if(blurMaterial == null)
				{
					if (blurShader == null)
						Validate();

					blurMaterial = new Material(blurShader) {hideFlags = HideFlags.DontSave};
					offsetHash = Shader.PropertyToID("_Offset");
				}

				return blurMaterial;
			}

			set
			{
				blurMaterial = value;
			}
		}

		public int PassIndex
		{
			get { return passIndex; }
			set { passIndex = value; }
		}

		public void Apply(RenderTexture target)
		{
			if(iterations == 0)
				return;

			//if (SystemInfo.supportsComputeShaders)
			//	ApplyComputeShader(target);
			//else
				ApplyPixelShader(target);
		}

		protected void ApplyComputeShader(RenderTexture target)
		{
			if (shaderWeights == null)
			{
				shaderWeights = new ComputeBuffer(4, 16);
				shaderWeights.SetData(new[]
				{
					new Color(0.324f, 0.324f, 0.324f, 1.0f),
					new Color(0.232f, 0.232f, 0.232f, 1.0f),
					new Color(0.0855f, 0.0855f, 0.0855f, 1.0f),
					new Color(0.0205f, 0.0205f, 0.0205f, 1.0f)
				});
			}

			int w = target.width;
			int kernelIndex = w == 128 ? computeShaderKernelIndex : (w == 256 ? computeShaderKernelIndex + 2 : computeShaderKernelIndex + 4);

			var temp = RenderTexturesCache.GetTemporary(target.width, target.height, 0, target.format, true, true);
			
			blurComputeShader.SetBuffer(kernelIndex, "weights", shaderWeights);
			blurComputeShader.SetTexture(kernelIndex, "_MainTex", target);
			blurComputeShader.SetTexture(kernelIndex, "_Output", temp);
			blurComputeShader.Dispatch(kernelIndex, 1, target.height, 1);

			++kernelIndex;

			blurComputeShader.SetBuffer(kernelIndex, "weights", shaderWeights);
			blurComputeShader.SetTexture(kernelIndex, "_MainTex", temp);
			blurComputeShader.SetTexture(kernelIndex, "_Output", target);
			blurComputeShader.Dispatch(kernelIndex, target.width, 1, 1);

			temp.Dispose();
		}

		protected void ApplyPixelShader(RenderTexture target)
		{
			var blurMaterial = BlurMaterial;

			var originalFilterMode = target.filterMode;
			target.filterMode = FilterMode.Bilinear;

			var temp = RenderTexture.GetTemporary(target.width, target.height, 0, target.format);
			temp.filterMode = FilterMode.Bilinear;

			for (int i = 0; i < iterations; ++i)
			{
				float blurSize = size*(1.0f + i*0.5f);

				blurMaterial.SetVector(offsetHash, new Vector4(blurSize, 0.0f, 0.0f, 0.0f));
				Graphics.Blit(target, temp, blurMaterial, passIndex);

				blurMaterial.SetVector(offsetHash, new Vector4(0.0f, blurSize, 0.0f, 0.0f));
				Graphics.Blit(temp, target, blurMaterial, passIndex);
			}

			target.filterMode = originalFilterMode;

			RenderTexture.ReleaseTemporary(temp);
		}

		public void Validate()
		{
			Validate("PlayWay Water/Utilities/Blur", "Shaders/Blurs");
		}

		public virtual void Validate(string shaderName, string computeShaderName = null, int kernelIndex = 0)
		{
			blurShader = Shader.Find(shaderName);

			if(computeShaderName != null)
			{
				blurComputeShader = Resources.Load<ComputeShader>(computeShaderName);
				computeShaderKernelIndex = kernelIndex;
			}
			else
				blurComputeShader = null;
		}

		public void Dispose()
		{
			if(blurMaterial != null)
				Object.DestroyImmediate(blurMaterial);

			if (shaderWeights != null)
			{
				shaderWeights.Release();
				shaderWeights = null;
			}
		}
	}
}
