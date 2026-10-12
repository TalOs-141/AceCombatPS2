// Sea surface. Instead of scrolling a picture of waves, it shades a flat
// plane as if it were rippled: layers of small waves drift across each
// other, the surface reflects the sky more strongly at glancing angles, the
// sun glitters on it, and the colour shifts from deep blue offshore to
// turquoise in the shallows with a thin line of foam at the beach.
Shader "AceCombat/Water"
{
    Properties
    {
        _DeepColor ("Deep water", Color) = (0.02, 0.10, 0.19, 1)
        _ShallowColor ("Shallow water", Color) = (0.10, 0.36, 0.38, 1)
        _FoamColor ("Foam", Color) = (0.90, 0.94, 0.95, 1)
        [NoScaleOffset] _WaveNormals ("Wave normals", 2D) = "bump" {}
        [NoScaleOffset] _ShoreDepth ("Shore depth (0 = waterline, 1 = deep)", 2D) = "white" {}
        _WorldSize ("World size (m)", Float) = 40000
        _WaveSize ("Wave sizes (m): ripple, chop, swell", Vector) = (38, 130, 900, 0)
        _WaveSpeed ("Wave drift (m/s)", Vector) = (1.6, 0.9, 2.5, 0)
        _WaveStrength ("Ripple strength", Range(0, 1)) = 0.28
        _CalmDistance ("Distance at which ripples fade out (m)", Float) = 9000
        _Reflectivity ("Sky reflection", Range(0, 1)) = 0.75
        _SunGlitter ("Sun glitter", Range(0, 4)) = 1.4
        _SunSharpness ("Sun glitter sharpness", Float) = 260
        _Foam ("Shoreline foam", Range(0, 1)) = 1
    }

    SubShader
    {
        // Drawn just before other see-through things: the surface fades out toward the shore,
        // letting the real shoreline underneath show through.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _FoamColor;
            float4 _WaveSize;
            float4 _WaveSpeed;
            float _WorldSize;
            float _WaveStrength;
            float _CalmDistance;
            float _Reflectivity;
            float _SunGlitter;
            float _SunSharpness;
            float _Foam;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_WaveNormals);
            SAMPLER(sampler_WaveNormals);
            TEXTURE2D(_ShoreDepth);
            SAMPLER(sampler_ShoreDepth);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            // The wave texture stores a surface slope in its red and green channels.
            float2 Ripple(float2 worldXZ, float size, float2 drift)
            {
                float2 uv = (worldXZ + drift * _Time.y) / size;
                return SAMPLE_TEXTURE2D(_WaveNormals, sampler_WaveNormals, uv).rg * 2.0 - 1.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 toCamera = _WorldSpaceCameraPos - input.positionWS;
                float range = length(toCamera);
                float3 view = toCamera / range;
                float2 xz = input.positionWS.xz;

                // Three wave layers moving in different directions; where they cross, the surface looks alive.
                float2 ripple = Ripple(xz, _WaveSize.x, float2(_WaveSpeed.x, _WaveSpeed.x * 0.4));
                float2 chop = Ripple(xz.yx, _WaveSize.y, float2(-_WaveSpeed.y * 0.6, _WaveSpeed.y));
                float2 swell = Ripple(xz, _WaveSize.z, float2(_WaveSpeed.z * 0.3, -_WaveSpeed.z));

                // Far away, individual ripples are smaller than a pixel and would only sparkle noisily, so they are calmed.
                float calm = saturate(1.0 - range / _CalmDistance);
                float2 slope = (ripple * calm * calm + chop * calm + swell * 0.6) * _WaveStrength;
                float3 normal = normalize(float3(-slope.x, 1.0, -slope.y));

                // Depth-based colour, and foam where the water runs out onto the beach.
                float2 mapUV = xz / _WorldSize + 0.5;
                float depth = SAMPLE_TEXTURE2D(_ShoreDepth, sampler_ShoreDepth, mapUV).r;
                // Beyond the mapped area it is all open sea.
                if (mapUV.x < 0.0 || mapUV.x > 1.0 || mapUV.y < 0.0 || mapUV.y > 1.0) depth = 1.0;
                float3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(pow(depth, 0.6)));

                Light sun = GetMainLight();
                float diffuse = saturate(dot(normal, sun.direction)) * 0.5 + 0.5;
                water *= diffuse * sun.color;

                // Water is a poor mirror looking straight down and a good one near the horizon.
                float facing = saturate(dot(normal, view));
                float fresnel = 0.03 + 0.97 * pow(1.0 - facing, 5.0);
                float3 sky = GlossyEnvironmentReflection(reflect(-view, normal), 0.08, 1.0);
                float3 color = lerp(water, sky, fresnel * _Reflectivity);

                float3 halfway = normalize(sun.direction + view);
                float glitter = pow(saturate(dot(normal, halfway)), _SunSharpness) * _SunGlitter;
                color += sun.color * glitter;

                float foamNoise = ripple.x * 0.5 + chop.y * 0.5;
                float foam = saturate(1.0 - depth * 9.0 + foamNoise * 0.45) * step(depth, 0.2);
                color = lerp(color, _FoamColor.rgb * sun.color, saturate(foam) * 0.85 * _Foam);

                color = MixFog(color, input.fogFactor);
                return half4(color, saturate(depth * 3.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 DepthFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
