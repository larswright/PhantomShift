Shader "Lars/URP/SoftEdgeAdditive"
{
    Properties
    {
        _Color      ("Color", Color) = (1,1,1,1)
        _BaseAlpha  ("Base Alpha", Range(0,1)) = 0.2
        _EdgePower  ("Edge Power (Fresnel)", Range(0.1,8)) = 2.5
        _EdgeMin    ("Edge Min Clamp", Range(0,1)) = 0.0
        _LengthFade ("Length Fade (UV.y->1 = weaker)", Range(0,1)) = 0.5

        // New realism controls
        _Intensity      ("Intensity", Range(0,100)) = 8.0
        _UseWorldSource ("Use _SourcePos (0/1)", Float) = 0
        _SourcePos      ("Source Pos (World)", Vector) = (0,0,0,1)
        _AttenRadius    ("Attenuation Radius", Float) = 6.0
        _Anisotropy     ("Forward Scattering g (-0.2..0.9)", Range(-0.2,0.9)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalRenderPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        // Additive: ideal for beams/volumetrics look-alikes.
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off // 2-sided: render inside of cone

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define PI 3.14159265359

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0; // UV.y along length (0->1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 posWS      : TEXCOORD3;
                float3 axisWS     : TEXCOORD4; // beam axis (object +Z in world)
            };

            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float  _BaseAlpha;
            float  _EdgePower;
            float  _EdgeMin;
            float  _LengthFade;

            // New
            float  _Intensity;
            float4 _SourcePos;
            float  _AttenRadius;
            float  _UseWorldSource; // 0 or 1
            float  _Anisotropy;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS   = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS     = _WorldSpaceCameraPos - posWS;
                o.uv         = v.uv;
                o.posWS      = posWS;

                // Object's +Z taken as beam direction in world space
                float3x3 m = (float3x3)unity_ObjectToWorld;
                o.axisWS = normalize(mul(m, float3(0,0,1)));

                return o;
            }

            // Henyey-Greenstein (unnormalized; we omit 1/(4π) and rely on _Intensity)
            float HG_Phase(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / max(1e-3, pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 1) "Soft edge" core mask (center brighter, edges softer)
                float3 N = normalize(i.normalWS);
                float3 V = normalize(i.viewWS);
                float ndv = saturate(dot(N, V));            // 0=edge, 1=center
                float centerMask = pow(ndv, _EdgePower);
                centerMask = max(centerMask, _EdgeMin);

                // 2) Axial fade along beam length (UV.y: 0 near source, 1 at tip)
                float axial = lerp(1.0, 0.0, saturate(i.uv.y)) * (1.0 - _LengthFade) + 1e-5;

                // 3) Distance attenuation from source (inverse-square style)
                //    Fallback origin: object pivot in world space
                float3 objOriginWS = float3(unity_ObjectToWorld._m03, unity_ObjectToWorld._m13, unity_ObjectToWorld._m23);
                float3 sourceWS = lerp(objOriginWS, _SourcePos.xyz, saturate(_UseWorldSource));
                float  d = distance(i.posWS, sourceWS);
                float  r = max(_AttenRadius, 1e-3);
                float  atten = 1.0 / (1.0 + (d*d) / (r*r)); // smooth, cheap ≈ 1/(1+(d/r)^2)

                // 4) Forward scattering: brighter when looking along the beam
                //    cosθ between viewer direction and beam direction
                float cosTheta = saturate(dot(-V, normalize(i.axisWS)));
                float phase = HG_Phase(cosTheta, _Anisotropy);

                // Final intensity in additive
                float alpha = _Intensity * _BaseAlpha * centerMask * axial * atten * phase;

                float3 rgb = _Color.rgb * alpha;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
