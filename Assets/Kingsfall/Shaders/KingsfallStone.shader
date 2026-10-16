// Kingsfall procedural stone: marble / obsidian with veins, gold trim (vertex colour G),
// glowing broken interiors (vertex colour R), dissolve, hit flash and rim glow.
Shader "Kingsfall/Stone"
{
    Properties
    {
        _BaseColor ("Base colour", Color) = (0.9, 0.88, 0.84, 1)
        _BaseColor2 ("Base colour 2 (cloud)", Color) = (0.8, 0.78, 0.74, 1)
        _VeinColor ("Vein colour", Color) = (0.35, 0.33, 0.32, 1)
        _VeinScale ("Vein scale", Float) = 3
        _VeinSharpness ("Vein sharpness", Float) = 8
        _VeinWarp ("Vein warp", Float) = 1.4
        _VeinAmount ("Vein amount", Range(0,1)) = 0.8
        _Smoothness ("Smoothness", Range(0,1)) = 0.85
        _VeinSmoothness ("Vein smoothness", Range(0,1)) = 0.6
        _Metallic ("Metallic", Range(0,1)) = 0
        [HDR] _VeinEmission ("Vein emission", Color) = (0,0,0,0)
        _EmissionPulse ("Emission pulse", Float) = 0
        _GoldColor ("Gold colour", Color) = (1.0, 0.74, 0.32, 1)
        _GoldSmoothness ("Gold smoothness", Range(0,1)) = 0.88
        [HDR] _InteriorColor ("Interior glow", Color) = (3, 0.9, 0.2, 1)
        _UseVertexColor ("Use vertex colour masks", Float) = 0
        _UseWorldSpace ("Pattern in world space", Float) = 0
        _NoiseOffset ("Noise offset", Vector) = (0,0,0,0)
        _TileSize ("Grout tile size (0 off)", Float) = 0
        _GroutWidth ("Grout width", Float) = 0.03
        _GroutColor ("Grout colour", Color) = (0.05,0.05,0.05,1)
        _Bumpiness ("Surface bumpiness", Float) = 0
        _Dissolve ("Dissolve", Range(0,1)) = 0
        [HDR] _DissolveEdge ("Dissolve edge", Color) = (4, 3, 1.2, 1)
        [HDR] _FlashColor ("Flash colour", Color) = (6, 6, 6, 1)
        _Flash ("Flash", Range(0,1)) = 0
        [HDR] _GlowColor ("Rim glow colour", Color) = (1.2, 0.9, 0.3, 1)
        _Glow ("Rim glow", Range(0,4)) = 0
        _Crack ("Crack amount", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _BaseColor2;
            half4 _VeinColor;
            float _VeinScale;
            float _VeinSharpness;
            float _VeinWarp;
            half _VeinAmount;
            half _Smoothness;
            half _VeinSmoothness;
            half _Metallic;
            half4 _VeinEmission;
            float _EmissionPulse;
            half4 _GoldColor;
            half _GoldSmoothness;
            half4 _InteriorColor;
            float _UseVertexColor;
            float _UseWorldSpace;
            float4 _NoiseOffset;
            float _TileSize;
            float _GroutWidth;
            half4 _GroutColor;
            float _Bumpiness;
            half _Dissolve;
            half4 _DissolveEdge;
            half4 _FlashColor;
            half _Flash;
            half4 _GlowColor;
            half _Glow;
            half _Crack;
        CBUFFER_END

        float KHash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float KNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
            return lerp(lerp(lerp(KHash(i + float3(0,0,0)), KHash(i + float3(1,0,0)), f.x),
                             lerp(KHash(i + float3(0,1,0)), KHash(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(KHash(i + float3(0,0,1)), KHash(i + float3(1,0,1)), f.x),
                             lerp(KHash(i + float3(0,1,1)), KHash(i + float3(1,1,1)), f.x), f.y), f.z);
        }

        float KFbm(float3 p)
        {
            float v = 0.0;
            float a = 0.5;
            [unroll] for (int o = 0; o < 5; o++)
            {
                v += a * KNoise(p);
                p = p * 2.03 + float3(1.7, 9.2, 3.1);
                a *= 0.5;
            }
            return v;
        }

        float3 PatternPos(float3 positionOS, float3 positionWS)
        {
            return (_UseWorldSpace > 0.5 ? positionWS : positionOS) + _NoiseOffset.xyz;
        }

        // Dissolve test shared by every pass.
        void DissolveClip(float3 p)
        {
            if (_Dissolve > 0.001)
            {
                float d = KFbm(p * 5.0 + 3.0);
                clip(d + 0.02 - _Dissolve * 0.92);
            }
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float4 color : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs vpi = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = vpi.positionCS;
                o.positionWS = vpi.positionWS;
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = _UseVertexColor > 0.5 ? v.color : float4(0, 0, 0, 1);
                o.fogFactor = ComputeFogFactor(vpi.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = PatternPos(i.positionOS, i.positionWS);
                DissolveClip(p);

                float3 normalWS = normalize(i.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // --- marble ---
                float3 q = p * _VeinScale;
                float warp = KFbm(q * 0.55);
                float cloud = KFbm(q * 1.3 + 11.0);
                float v1 = sin(q.x * 1.7 + q.y * 1.1 + q.z * 0.6 + warp * _VeinWarp * 7.0);
                float v2 = sin(q.z * 2.9 - q.y * 1.9 + q.x * 0.4 + KFbm(q * 1.1 + 5.0) * _VeinWarp * 9.0);
                float vein = pow(saturate(1.0 - abs(v1)), _VeinSharpness);
                float vein2 = pow(saturate(1.0 - abs(v2)), _VeinSharpness * 2.5) * 0.6;
                float veinMask = saturate(max(vein, vein2)) * _VeinAmount;

                half3 albedo = lerp(_BaseColor.rgb, _BaseColor2.rgb, smoothstep(0.3, 0.75, cloud));
                albedo = lerp(albedo, _VeinColor.rgb, veinMask);
                half smooth = lerp(_Smoothness, _VeinSmoothness, veinMask) * (0.92 + 0.08 * cloud);
                half metallic = _Metallic;
                half3 emission = _VeinEmission.rgb * pow(veinMask, 1.6) *
                                 (1.0 + _EmissionPulse * (0.35 * sin(_Time.y * 1.7 + p.y * 6.0 + warp * 8.0)));

                // --- floor grout ---
                if (_TileSize > 0.0)
                {
                    float2 t = i.positionWS.xz / _TileSize;
                    float2 row = float2(t.x + (fmod(floor(t.y), 2.0) * 0.5), t.y);
                    float2 g = abs(frac(row) - 0.5);
                    float grout = smoothstep(0.5 - _GroutWidth, 0.5 - _GroutWidth * 0.4, max(g.x, g.y));
                    float tileTint = KHash(float3(floor(row), 7.0));
                    albedo *= 0.82 + 0.3 * tileTint;
                    albedo = lerp(albedo, _GroutColor.rgb, grout);
                    smooth *= (1.0 - grout) * (0.6 + 0.4 * tileTint);
                }

                // --- cheap surface relief ---
                if (_Bumpiness > 0.0)
                {
                    float e = 0.02;
                    float3 bp = i.positionWS * 9.0;
                    float b0 = KFbm(bp);
                    float3 grad = float3(KFbm(bp + float3(e,0,0)) - b0, KFbm(bp + float3(0,e,0)) - b0, KFbm(bp + float3(0,0,e)) - b0) / e;
                    normalWS = normalize(normalWS - (grad - dot(grad, normalWS) * normalWS) * _Bumpiness * 0.02);
                }

                // --- gold trim ---
                half gold = saturate(i.color.g);
                if (gold > 0.01)
                {
                    half3 goldAlbedo = _GoldColor.rgb * (0.85 + 0.25 * cloud);
                    albedo = lerp(albedo, goldAlbedo, gold);
                    metallic = lerp(metallic, 1.0, gold);
                    smooth = lerp(smooth, _GoldSmoothness, gold);
                    emission *= (1.0 - gold);
                }

                // --- broken interior ---
                half interior = saturate(i.color.r);
                if (interior > 0.01)
                {
                    float n = KFbm(p * 9.0 + _Time.y * 0.6);
                    albedo = lerp(albedo, albedo * 0.25, interior);
                    smooth = lerp(smooth, 0.25, interior);
                    emission += _InteriorColor.rgb * interior * (0.35 + 1.1 * n * n);
                }

                // --- cracks (victim taking damage) ---
                if (_Crack > 0.001)
                {
                    float c = abs(KNoise(p * 7.0) - 0.5) + abs(KNoise(p * 15.0 + 3.0) - 0.5) * 0.5;
                    float crack = smoothstep(0.035 * _Crack, 0.0, c) * saturate(_Crack * 1.5);
                    albedo *= 1.0 - crack * 0.9;
                    emission += _InteriorColor.rgb * crack * 1.6;
                }

                // --- dissolve rim ---
                if (_Dissolve > 0.001)
                {
                    float d = KFbm(p * 5.0 + 3.0) + 0.02 - _Dissolve * 0.92;
                    emission += _DissolveEdge.rgb * smoothstep(0.08, 0.0, d);
                }

                // --- rim glow & flash ---
                half fres = pow(1.0 - saturate(dot(normalWS, viewDir)), 3.0);
                emission += _GlowColor.rgb * fres * _Glow;
                emission += _FlashColor.rgb * _Flash;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.metallic = metallic;
                s.smoothness = smooth;
                s.occlusion = 1.0;
                s.emission = emission;
                s.alpha = 1.0;
                s.normalTS = half3(0, 0, 1);
                s.specular = half3(0, 0, 0);

                half4 col = UniversalFragmentPBR(inputData, s);
                col.rgb = MixFog(col.rgb, inputData.fogCoord);
                col.a = 1.0;
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                o.positionCS = ApplyShadowClamping(positionCS);
                o.positionOS = v.positionOS.xyz;
                o.positionWS = positionWS;
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(PatternPos(i.positionOS, i.positionWS));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(PatternPos(i.positionOS, i.positionWS));
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DNVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = v.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 DNFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(PatternPos(i.positionOS, i.positionWS));
                float3 n = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
