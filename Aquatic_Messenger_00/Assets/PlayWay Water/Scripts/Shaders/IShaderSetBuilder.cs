using UnityEngine;

namespace PlayWay.Water
{
	public interface IShaderSetBuilder
	{
		Shader BuildShaderVariant(string[] localKeywords, string[] sharedKeywords, string additionalCode, string keywordsString, bool volume, bool useForwardPasses, bool useDeferredPass);
	}
}
