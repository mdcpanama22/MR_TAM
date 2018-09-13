Shader "Custom/Jelly Distortion TESS Transparent" {
        Properties {
            _MainTex ("Base (RGB)", 2D) = "white" {}
            _EdgeLength ("Edge Length", range(0,15)) = 1 
			_Displacement ("Vert Displacement", range(0,10)) = .003
            _NormalMap ("Normalmap", 2D) = "bump" {}
            _Color ("Color", color) = (1,1,1,0)
            _SpecColor ("Spec color", color) = (0.5,0.5,0.5,0.5)
            _LightIntensity ("Light Intensity", range(0,5)) = 1.0
        }
        SubShader {
           	Tags { "Queue"="Transparent" "RenderType"="Transparent" }
            LOD 300

   			CGPROGRAM
            #pragma surface surf BlinnPhong vertex:disp tessellate:tessEdgeBased tessphong:_Phong alpha
            #pragma target 5.0
           	#include "Tessellation.cginc"
           	#include "UnityCG.cginc"
           	

	
			sampler2D _DispTex;
            float _Alpha;
			float _EdgeLength;
			float _Phong;
			float _Displacement;
			float4 _Color;
			
			uniform float4 unity_lightColor[4];
			
			  	
  


            struct appdata {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 tangent : TANGENT;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
            };
            
            float4 tessEdgeBased (appdata v0, appdata v1, appdata v2)
            {
            
                return UnityEdgeLengthBasedTess (v0.vertex, v1.vertex, v2.vertex,_EdgeLength);
            }
            
			float3 mod289(float3 x) {
			  return x - floor(x * (1.0 / 289.0)) * 289.0;
			}

			float4 mod289(float4 x) {
			  return x - floor(x * (1.0 / 289.0)) * 289.0;
			}

			float4 permute(float4 x) {
			     return mod289(((x*34.0)+1.0)*x);
			}

			float4 taylorInvSqrt(float4 r)
			{
			  return 1.79284291400159 - 0.85373472095314 * r;
			}
		
			
			float snoise(float3 v)
			  { 
			  const float2  C = float2(1.0/6.0, 1.0/3.0) ;
			  const float4  D = float4(0.0, 0.5, 1.0, 2.0);

			// First corner
			  float3 i  = floor(v + dot(v, C.yyy) );
			  float3 x0 =   v - i + dot(i, C.xxx) ;

			// Other corners
			  float3 g = step(x0.yzx, x0.xyz);
			  float3 l = 1.0 - g;
			  float3 i1 = min( g.xyz, l.zxy );
			  float3 i2 = max( g.xyz, l.zxy );

			  //   x0 = x0 - 0.0 + 0.0 * C.xxx;
			  //   x1 = x0 - i1  + 1.0 * C.xxx;
			  //   x2 = x0 - i2  + 2.0 * C.xxx;
			  //   x3 = x0 - 1.0 + 3.0 * C.xxx;
			  float3 x1 = x0 - i1 + C.xxx;
			  float3 x2 = x0 - i2 + C.yyy; // 2.0*C.x = 1/3 = C.y
			  float3 x3 = x0 - D.yyy;      // -1.0+3.0*C.x = -0.5 = -D.y

			// Permutations
			  i = mod289(i); 
			  float4 p = permute( permute( permute( 
			             i.z + float4(0.0, i1.z, i2.z, 1.0 ))
			           + i.y + float4(0.0, i1.y, i2.y, 1.0 )) 
			           + i.x + float4(0.0, i1.x, i2.x, 1.0 ));

			// Gradients: 7x7 points over a square, mapped onto an octahedron.
			// The ring size 17*17 = 289 is close to a multiple of 49 (49*6 = 294)
			  float n_ = 0.142857142857; // 1.0/7.0
			  float3  ns = n_ * D.wyz - D.xzx;

			  float4 j = p - 49.0 * floor(p * ns.z * ns.z);  //  mod(p,7*7)

			  float4 x_ = floor(j * ns.z);
			  float4 y_ = floor(j - 7.0 * x_ );    // mod(j,N)

			  float4 x = x_ *ns.x + ns.yyyy;
			  float4 y = y_ *ns.x + ns.yyyy;
			  float4 h = 1.0 - abs(x) - abs(y);

			  float4 b0 = float4( x.xy, y.xy );
			  float4 b1 = float4( x.zw, y.zw );

			  //float4 s0 = float4(lessThan(b0,0.0))*2.0 - 1.0;
			  //float4 s1 = float4(lessThan(b1,0.0))*2.0 - 1.0;
			  float4 s0 = floor(b0)*2.0 + 1.0;
			  float4 s1 = floor(b1)*2.0 + 1.0;
			  float4 sh = -step(h, float4(0.0,0.0,0.0,0.0));

			  float4 a0 = b0.xzyw + s0.xzyw*sh.xxyy ;
			  float4 a1 = b1.xzyw + s1.xzyw*sh.zzww ;

			  float3 p0 = float3(a0.xy,h.x);
			  float3 p1 = float3(a0.zw,h.y);
			  float3 p2 = float3(a1.xy,h.z);
			  float3 p3 = float3(a1.zw,h.w);

			//Normalise gradients
			  float4 norm = taylorInvSqrt(float4(dot(p0,p0), dot(p1,p1), dot(p2, p2), dot(p3,p3)));
			  p0 *= norm.x;
			  p1 *= norm.y;
			  p2 *= norm.z;
			  p3 *= norm.w;

			// lerp final noise value
			  float4 m = max(0.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
			  m = m * m;
			  return 42.0 * dot( m*m, float4( dot(p0,x0), dot(p1,x1), 
			                                dot(p2,x2), dot(p3,x3) ) );
			  }
			  
			half4 LightingSimple (SurfaceOutput s, half3 lightDir, half3 viewDir, half atten) {

				half4 c;
				half3 o = (1.0,1.0,1.0);
				
		        c.rgb = (s.Albedo - o.xyz);
		        c.a = s.Alpha;
		        return c;
	    	}
 
	 
		
            void disp (inout appdata v)
            {
            
   	
            		  	
		   		const int MAX_DISP = 1;

        	
        		const float e = 2.7182818284590452353602874713527 ;
		  	
		  		
		  		float mag = 0.0;
		  		float intensity = 0.0;
		  		int numLights = 0;
		  		
	
		  		
		
		  			
		  		for( int i = 0; i < 5; i++) {
		  				
		  				if(unity_LightColor[i].r > 0) {
		  					numLights++;
		  				}
		  				
		  				intensity += unity_LightColor[i].r;;

		  			
		  		}
		  		

		  		mag += intensity / numLights;
		  			
		  			
		
		  		//EDIT THIS JUSTIN
		  		float finalDisp = _Displacement;
		  		
   				float displacement  =  snoise(float3(1,1,1) * cos(sin(_Time))) * finalDisp;
   			
                v.vertex.xyz += v.normal *  displacement *	sin(tan(cos(_Time / 2))) / 2.5;
            

   			}
   			
      		struct Input {
          		float2 uv_MainTex;
          		float2 uv_BumpMap;
          		float3 viewDir;
      		};
      		
      		
		    sampler2D _MainTex;
		    sampler2D _NormalMap;
		    
		  	void surf (Input IN, inout SurfaceOutput o) {
		  	

        		const half MAX_SATURATION = 1.0;
        	
        		const float e = 2.7182818284590452353602874713527 ;
		  	
		  		
		  		float mag = 0.0;
		  		float intensity = 0.0;
		  		int numLights = 0;
		  		
	
		  		
		
		  			
		  		for( int i = 0; i < 5; i++) {
		  				
		  				if(unity_LightColor[i].r > 0) {
		  					numLights++;
		  				}
		  				
		  				intensity += unity_LightColor[i].r;

		  			
		  		}
		  		

		  		mag += intensity / numLights;
		  		
		  		float finalSaturation = MAX_SATURATION/(1+pow(e,-(MAX_SATURATION*(mag-1.0))));
		  		
	
				half4 c = tex2D (_MainTex, IN.uv_MainTex);
				
				
				float greyScale = dot(c.rgb, fixed3(.222, .707, .071));  // Convert to greyscale
				//c.rgb  = lerp(float3(greyScale, greyScale, greyScale), c.rgb,  finalSaturation  );
				
				o.Normal = UnpackNormal (tex2D (_NormalMap, IN.uv_BumpMap));
				o.Albedo = c.rgb;
				o.Alpha = _Color.a;
				//o.Alpha = .1;
			}
		
            ENDCG
        }
        FallBack "Custom/FLAT Jelly Distortion"
    }