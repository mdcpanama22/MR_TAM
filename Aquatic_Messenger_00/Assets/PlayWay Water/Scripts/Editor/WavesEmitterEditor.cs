using UnityEngine;
using UnityEditor;

namespace PlayWay.Water
{
	[CustomEditor(typeof(ComplexWavesEmitter))]
	public class WavesEmitterEditor : WaterEditor
	{
		public override void OnInspectorGUI()
		{
			PropertyField("wavesParticleSystem");

			var wavesSourceProp = PropertyField("wavesSource");
			var wavesSource = (ComplexWavesEmitter.WavesSource)wavesSourceProp.enumValueIndex;

			switch(wavesSource)
			{
				case ComplexWavesEmitter.WavesSource.CustomWaveFrequency:
				{
					PropertyField("wavelength");
					PropertyField("amplitude");
					PropertyField("emissionRate");
					PropertyField("width");
					PropertyField("waveShapeIrregularity");
					PropertyField("lifetime");
					PropertyField("shoreWaves");
					break;
				}

				case ComplexWavesEmitter.WavesSource.WindWavesSpectrum:
				{
					PropertyField("spectrumCoincidenceRange");
					PropertyField("spectrumWavesCount");
					PropertyField("span");
					PropertyField("waveShapeIrregularity");
					PropertyField("lifetime");
					PropertyField("emissionFrequencyScale");
					break;
				}

				case ComplexWavesEmitter.WavesSource.Shoaling:
				{
					PropertyField("boundsSize");
					PropertyField("span");
					PropertyField("lifetime");
					PropertyField("waveShapeIrregularity");
					PropertyField("spawnDepth");
					PropertyField("emissionFrequencyScale");
					PropertyField("spawnPointsDensity");
					serializedObject.FindProperty("shoreWaves").boolValue = true;

					break;
				}
			}

			serializedObject.ApplyModifiedProperties();
		}
	}
}
