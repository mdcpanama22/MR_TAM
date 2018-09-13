using UnityEngine;
using UnityEditor;

namespace PlayWay.Water
{
	[CustomEditor(typeof(WaveParticleSystem))]
	public class WavesParticleSystemEditor : WaterEditor
	{
		public override void OnInspectorGUI()
		{
			var target = (WaveParticleSystem)this.target;

			PropertyField("maxParticles");
			PropertyField("maxParticlesPerTile");
			PropertyField("prewarmTime");
			PropertyField("timePerFrame");

			if(Application.isPlaying)
			{
				GUI.enabled = false;
				EditorGUILayout.IntField("Particle Count", target.ParticleCount);
				GUI.enabled = true;
			}

			serializedObject.ApplyModifiedProperties();
		}

		public override bool RequiresConstantRepaint()
		{
			return Application.isPlaying;
		}
	}
}
