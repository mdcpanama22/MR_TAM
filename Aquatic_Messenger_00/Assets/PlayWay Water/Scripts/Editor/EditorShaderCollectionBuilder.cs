using UnityEngine;
using System.Linq;
using System.IO;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PlayWay.Water
{
	/// <summary>
	/// Builds shader collections. It's separated to editor script because it runs in less restrictive .net environment.
	/// </summary>
	public class EditorShaderCollectionBuilder : IShaderSetBuilder
	{
		private const string LocalKeywordDefinitionFormat = "#define {0} 1\r\n";
		private const string SharedKeywordDefinitionFormat = "#pragma multi_compile {0}\r\n";
		private const string ForwardPassesStart = "// START FORWARD_PASSES";
		private const string ForwardPassesEnd = "// END FORWARD_PASSES";
		private const string DeferredPassStart = "// START DEFERRED_PASS";
		private const string DeferredPassEnd = "// END DEFERRED_PASS";

		[InitializeOnLoadMethod]
		public static void RegisterShaderCollectionBuilder()
		{
			var instance = new EditorShaderCollectionBuilder();
			ShaderSet.shaderCollectionBuilder = instance;
		}
		
		public Shader BuildShaderVariant(string[] localKeywords, string[] sharedKeywords, string additionalCode, string keywordsString, bool volume, bool useForwardPasses, bool useDeferredPass)
		{
			string shaderPath;
            string shaderCodeTemplate = File.ReadAllText(!volume ? WaterPackageUtilities.WaterPackagePath + "/Shaders/Water/PlayWay Water (TEMPLATE).shader" : WaterPackageUtilities.WaterPackagePath + "/Shaders/Water/PlayWay Water - Volume (TEMPLATE).shader");
			string shaderCode = BuildShader(shaderCodeTemplate, localKeywords, sharedKeywords, additionalCode, volume, keywordsString, useForwardPasses, useDeferredPass);
			
			if(!volume)
				shaderPath = WaterPackageUtilities.WaterPackagePath + "/Shaders/Water/PlayWay Water Variation #" + HashString(keywordsString) + ".shader";
			else
				shaderPath = WaterPackageUtilities.WaterPackagePath + "/Shaders/Water/PlayWay Water Volume Variation #" + HashString(keywordsString) + ".shader";

			File.WriteAllText(shaderPath, shaderCode);
			AssetDatabase.Refresh();

			var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
			return shader;
		}

		public void CleanUpUnusedShaders()
		{
			var files = new List<string>(
				Directory.GetFiles(WaterPackageUtilities.WaterPackagePath + "/Shaders/Water/")
					.Where(f => f.Contains(" Variation ") && !f.EndsWith(".meta"))
				);

			var guids = AssetDatabase.FindAssets("t:ShaderSet", null);

			for (int i = 0; i < guids.Length; ++i)
			{
				var shaderCollection = AssetDatabase.LoadAssetAtPath<ShaderSet>(AssetDatabase.GUIDToAssetPath(guids[i]));

				var surfaceShaders = shaderCollection.SurfaceShaders;

				if (surfaceShaders != null)
				{
					for (int ii = 0; ii < surfaceShaders.Length; ++ii)
					{
						string shaderPath = AssetDatabase.GetAssetPath(surfaceShaders[ii]);
						files.Remove(shaderPath);
					}
				}

				var volumeShaders = shaderCollection.VolumeShaders;

				if(volumeShaders != null)
				{
					for(int ii = 0; ii < volumeShaders.Length; ++ii)
					{
						string shaderPath = AssetDatabase.GetAssetPath(volumeShaders[ii]);
						files.Remove(shaderPath);
					}
				}
			}

			for (int i = files.Count - 1; i >= 0; --i)
				AssetDatabase.DeleteAsset(files[i]);
		}

		private static string BuildShader(string code, string[] localKeywords, string[] sharedKeywords, string additionalCode, bool volume, string keywordsString, bool useForwardPasses, bool useDeferredPass)
		{
			var localKeywordsCode = localKeywords.Select(k => string.Format(LocalKeywordDefinitionFormat, k)).ToArray();
			var sharedKeywordsCode = sharedKeywords.Select(k => string.Format(SharedKeywordDefinitionFormat, k)).ToArray();

			string keywordsCode = string.Join("\t\t\t", localKeywordsCode) + "\r\n\t\t\t" + string.Join("\t\t\t", sharedKeywordsCode);

			if (!string.IsNullOrEmpty(additionalCode))
				keywordsCode += "\r\n\t\t\t" + additionalCode;

			if (!useForwardPasses)
			{
				int startIndex = code.IndexOf(ForwardPassesStart);
				int endIndex = code.IndexOf(ForwardPassesEnd) + ForwardPassesEnd.Length;

				if(startIndex != -1 && endIndex != -1)
					code = code.Remove(startIndex, endIndex - startIndex);
			}

			if(!useDeferredPass)
			{
				int startIndex = code.IndexOf(DeferredPassStart);
				int endIndex = code.IndexOf(DeferredPassEnd) + DeferredPassEnd.Length;

				if(startIndex != -1 && endIndex != -1)
					code = code.Remove(startIndex, endIndex - startIndex);
			}

			return code.Replace("PlayWay Water/Standard" + (volume ? " Volume" : ""), "PlayWay Water/Variations/Water " + (volume ? "Volume " : "") + keywordsString)
				.Replace("#define PLACE_KEYWORDS_HERE", keywordsCode);
		}

		private static int HashString(string text)
		{
			int len = text.Length;
			int hash = 23;

			for(int i = 0; i < len; ++i)
				hash = hash * 31 + text[i];

			return hash;
		}
	}

	public class WaterShadersCleanupTask : UnityEditor.AssetModificationProcessor
	{
		public static string[] OnWillSaveAssets(string[] paths)
		{
			var shaderCollectionBuilder = (EditorShaderCollectionBuilder)ShaderSet.shaderCollectionBuilder;
			shaderCollectionBuilder.CleanUpUnusedShaders();

			return paths;
		}
	}
}