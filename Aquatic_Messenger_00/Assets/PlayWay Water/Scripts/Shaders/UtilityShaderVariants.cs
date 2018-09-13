using System.Collections.Generic;
using UnityEngine;

namespace PlayWay.Water.Internal
{
	public class UtilityShaderVariants
	{
		private readonly Dictionary<int, Material> materials;

		private static UtilityShaderVariants instance;
		public static UtilityShaderVariants Instance
		{
			get { return instance ?? (instance = new UtilityShaderVariants()); }
		}

		private UtilityShaderVariants()
		{
			this.materials = new Dictionary<int, Material>();
		}

		public Material GetVariant(Shader shader, string keywords)
		{
			Material material;

			int hash = shader.GetInstanceID() ^ keywords.GetHashCode();

			if (!materials.TryGetValue(hash, out material))
			{
				material = new Material(shader)
				{
					hideFlags = HideFlags.DontSave,
					shaderKeywords = keywords.Split(' ')
				};

				materials[hash] = material;
			}

			return material;
		}
	}
}
