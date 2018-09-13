using PlayWay.Water;
using UnityEngine;
using UnityEditor;

namespace PlayWay.WaterEditor
{
	[CustomEditor(typeof(ShaderSet))]
	public class ShaderCollectionEditor : Editor
	{
		private ShaderSet temporaryShaderCollection;
		private Editor nestedEditor;
		private bool modified;

		public override void OnInspectorGUI()
		{
			if (temporaryShaderCollection == null)
			{
				temporaryShaderCollection = CreateInstance<ShaderSet>();
				EditorUtility.CopySerialized(target, temporaryShaderCollection);
			}

			CreateCachedEditor(temporaryShaderCollection, typeof(Editor), ref nestedEditor);
			nestedEditor.OnInspectorGUI();

			if (GUI.changed)
				modified = true;

			if (modified)
			{
				GUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();

				if (GUILayout.Button("Apply changes", GUILayout.Width(140.0f)))
				{
					EditorUtility.CopySerialized(temporaryShaderCollection, target);

					var shaderCollection = (ShaderSet) target;
					shaderCollection.Build();

					EditorUtility.CopySerialized(target, temporaryShaderCollection);

					modified = false;
				}

				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}

		private void OnDisable()
		{
			DestroyImmediate(nestedEditor);
			nestedEditor = null;
		}
	}
}
