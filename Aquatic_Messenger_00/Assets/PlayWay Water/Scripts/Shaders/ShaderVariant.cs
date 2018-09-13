using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace PlayWay.Water
{
	public class ShaderVariant
	{
		private readonly Dictionary<string, bool> unityKeywords;
		private readonly Dictionary<string, bool> waterKeywords;
		private readonly Dictionary<string, string> surfaceShaderParts;
		private readonly Dictionary<string, string> volumeShaderParts;

		public ShaderVariant()
		{
			unityKeywords = new Dictionary<string, bool>();
			waterKeywords = new Dictionary<string, bool>();
			surfaceShaderParts = new Dictionary<string, string>();
			volumeShaderParts = new Dictionary<string, string>();
		}

		public void SetUnityKeyword(string keyword, bool value)
		{
			if (value)
				unityKeywords[keyword] = true;
			else
				unityKeywords.Remove(keyword);
		}

		public void SetWaterKeyword(string keyword, bool value)
		{
			if (value)
				waterKeywords[keyword] = true;
			else
				waterKeywords.Remove(keyword);
		}

		public void SetAdditionalSurfaceCode(string keyword, string code)
		{
			if (code != null)
				surfaceShaderParts[keyword] = code;
			else
				surfaceShaderParts.Remove(keyword);
		}

		public void SetAdditionalVolumeCode(string keyword, string code)
		{
			if(code != null)
				volumeShaderParts[keyword] = code;
			else
				volumeShaderParts.Remove(keyword);
		}

		public bool IsUnityKeywordEnabled(string keyword)
		{
			bool value;

			if (unityKeywords.TryGetValue(keyword, out value))
				return true;

			return false;
		}

		public bool IsWaterKeywordEnabled(string keyword)
		{
			bool value;

			if (waterKeywords.TryGetValue(keyword, out value))
				return true;

			return false;
		}

		public string GetAdditionalSurfaceCode()
		{
			StringBuilder sb = new StringBuilder(512);

			foreach (string code in surfaceShaderParts.Values)
				sb.Append(code);

			return sb.ToString();
		}

		public string GetAdditionalVolumeCode()
		{
			StringBuilder sb = new StringBuilder(512);

			foreach(string code in volumeShaderParts.Values)
				sb.Append(code);

			return sb.ToString();
		}

		public string[] GetUnityKeywords()
		{
			string[] keywords = new string[unityKeywords.Count];
			int index = 0;

			foreach (string keyword in unityKeywords.Keys)
				keywords[index++] = keyword;

			return keywords;
		}

		public string[] GetWaterKeywords()
		{
			string[] keywords = new string[waterKeywords.Count];
			int index = 0;

			foreach (string keyword in waterKeywords.Keys)
				keywords[index++] = keyword;

			return keywords;
		}

		public string GetKeywordsString()
		{
			StringBuilder sb = new StringBuilder(512);
			bool notFirst = false;

			foreach (string keyword in waterKeywords.Keys.OrderBy(k => k))
			{
				if (notFirst)
					sb.Append(' ');
				else
					notFirst = true;

				sb.Append(keyword);
			}

			foreach (string keyword in unityKeywords.Keys.OrderBy(k => k))
			{
				if (notFirst)
					sb.Append(' ');
				else
					notFirst = true;

				sb.Append(keyword);
			}

			foreach (string keyword in surfaceShaderParts.Keys.OrderBy(k => k))
			{
				if (notFirst)
					sb.Append(' ');
				else
					notFirst = true;

				sb.Append(keyword);
			}

			return sb.ToString();
		}
	}
}
