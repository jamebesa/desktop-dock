// Used by DockTransparencyMode.PerPixelAlpha. Turns a captured backbuffer into what Windows' UpdateLayeredWindow
// expects: color premultiplied by alpha in display (gamma) space, with no channel brighter than alpha.
Shader "Hidden/DesktopDock/AlphaPrepare"
{
    Properties
    {
        _MainTex ("Captured frame", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma multi_compile_local _ DESKTOPDOCK_LINEAR
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float3 ToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
            float3 ToGamma(float3 c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }

            fixed4 frag(v2f_img i) : SV_Target
            {
                // The capture texture is created without sRGB conversion, so this is the backbuffer's bytes / 255.
                float4 c = tex2D(_MainTex, i.uv);
            #if DESKTOPDOCK_LINEAR
                // Linear projects blend in linear space and store the result sRGB-encoded: rgb = enc(a * color).
                // The desktop compositor blends in display space, so it needs a * enc(color).
                float3 premul = ToLinear(c.rgb);
            #else
                float3 premul = c.rgb;
            #endif
                // Premultiplied color can't be brighter than its alpha, or it adds light instead of blending. Shaders
                // that also blend alpha with SrcAlpha leave alpha too low (a * a), so treat a pixel as at least as
                // opaque as it is bright.
                float a = max(c.a, max(premul.r, max(premul.g, premul.b)));
            #if DESKTOPDOCK_LINEAR
                c.rgb = a > 0.0 ? ToGamma(premul / a) * a : 0.0;
            #endif
                return float4(c.rgb, a);
            }
            ENDCG
        }
    }
}
