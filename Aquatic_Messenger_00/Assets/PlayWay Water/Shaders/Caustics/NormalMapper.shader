﻿Shader "PlayWay Water/Caustics/NormalMapper"
{
	Properties
	{
		_DisplacementNormalsIntensity ("Displacement Normals Intensity", Float) = 1.4
		_GlobalNormalMap ("", 2D) = "black" {}
		_GlobalNormalMap1 ("", 2D) = "black" {}
		_GlobalNormalMap2 ("", 2D) = "black" {}
		_GlobalNormalMap3 ("", 2D) = "black" {}
		_GlobalDisplacementMap ("", 2D) = "black" {}
		_GlobalDisplacementMap1 ("", 2D) = "black" {}
		_GlobalDisplacementMap2 ("", 2D) = "black" {}
		_GlobalDisplacementMap3 ("", 2D) = "black" {}
	}

	SubShader
	{
		Tags { "CustomType"="Water" }

		Pass
		{
			ZWrite Off
			ZTest Always
			Cull Off
			
			CGPROGRAM
			#if UNITY_CAN_COMPILE_TESSELLATION
				#pragma vertex tessvert_surf
				#pragma fragment frag

				#pragma hull hs_surf
				#pragma domain ds_surf
			#endif

			#define POST_TESS_VERT vert
			#define TESS_OUTPUT v2f
			#define _WATER_OVERLAYS 1
			#define UNIFORM_TESSELATION 1

			#pragma multi_compile __ _WAVES_FFT

			#pragma target 5.0
			
			#include "../Caustics/NormalMapper.cginc"
			#include "../Includes/WaterTessellation.cginc"
			
			ENDCG
		}
	}

	SubShader
	{
		Tags { "CustomType"="Water" }

		Pass
		{
			ZWrite Off
			ZTest Always
			Cull Off
			
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#define _WATER_OVERLAYS 1
			
			#pragma multi_compile __ _WAVES_FFT
			#pragma target 3.0
			
			#include "../Caustics/NormalMapper.cginc"
			
			ENDCG
		}
	}
}
