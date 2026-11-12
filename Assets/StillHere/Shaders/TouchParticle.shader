Shader "StillHere/TouchParticle"
{
    // Faint white motes that sit on the surface of whatever was touched.
    // Each mote wanders around a fixed anchor, so the outline holds its shape while never being still.
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _SHNow;
            float _SHWander;

            struct Attributes
            {
                float3 pos : POSITION;       // world-space anchor
                float2 corner : TEXCOORD0;   // quad corner, -1..1
                float4 data : TEXCOORD1;     // birth, life, size, brightness
                float4 seed : TEXCOORD2;     // random 0..1
            };
            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float a : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float birth = v.data.x, life = v.data.y, size = v.data.z, bright = v.data.w;
                float age = _SHNow - birth;
                float a = 0;
                if (age > 0 && age < life)
                    a = smoothstep(0.0, 0.25, age) * (1.0 - smoothstep(life * 0.35, life, age));

                float t = _SHNow;
                float3 s = v.seed.xyz * 6.2831853;
                float w = _SHWander * (0.55 + 0.9 * v.seed.w);
                float3 off = float3(
                    sin(t * 1.31 + s.x) + 0.6 * sin(t * 2.17 + s.y * 1.7),
                    sin(t * 1.07 + s.y) + 0.6 * sin(t * 1.93 + s.z * 1.3),
                    sin(t * 1.19 + s.z) + 0.6 * sin(t * 2.41 + s.x * 1.9)) * w;
                float twinkle = 0.72 + 0.28 * sin(t * 3.1 + s.x * 5.0);

                float3 vp = TransformWorldToView(v.pos + off);
                float d = max(-vp.z, 0.05);
                float sz = clamp(size, d * 0.0011, d * 0.0055);
                vp.xy += v.corner * sz;
                o.pos = TransformWViewToHClip(vp);
                o.uv = v.corner;
                o.a = a * bright * twinkle;
                if (a <= 0.002) o.pos = float4(2, 2, 2, 1);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r = dot(i.uv, i.uv);
                clip(1.0 - r);
                float f = 1.0 - r;
                return half4(0.93, 0.95, 1.0, i.a * f * 0.75);
            }
            ENDHLSL
        }
    }
}
