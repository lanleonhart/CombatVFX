// Bloom for the Effects Studio: pass 0 keeps what is brighter than the threshold (soft knee), pass 1 is a 9-tap
// gaussian along _Offset, pass 2 adds _Bloom × _Intensity onto the source.
Shader "GNFX/Bloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _Bloom ("Bloom", 2D) = "black" {}
        _Threshold ("Threshold", Float) = 1.0
        _Intensity ("Intensity", Float) = 1.0
        _Offset ("Offset", Vector) = (0, 0, 0, 0)
    }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _Bloom;
    float _Threshold, _Intensity;
    float4 _Offset;
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
    float4 fragThreshold(v2f i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        float br = max(c.r, max(c.g, c.b));
        float knee = 0.5;
        float soft = clamp(br - _Threshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee + 1e-5);
        float contrib = max(soft, br - _Threshold) / max(br, 1e-5);
        return float4(c.rgb * contrib, 1.0);
    }
    float4 fragBlur(v2f i) : SV_Target
    {
        float2 o = _Offset.xy;
        float4 s = tex2D(_MainTex, i.uv) * 0.2270;
        s += (tex2D(_MainTex, i.uv + o) + tex2D(_MainTex, i.uv - o)) * 0.1945;
        s += (tex2D(_MainTex, i.uv + 2.0 * o) + tex2D(_MainTex, i.uv - 2.0 * o)) * 0.1216;
        s += (tex2D(_MainTex, i.uv + 3.0 * o) + tex2D(_MainTex, i.uv - 3.0 * o)) * 0.0540;
        s += (tex2D(_MainTex, i.uv + 4.0 * o) + tex2D(_MainTex, i.uv - 4.0 * o)) * 0.0162;
        return s;
    }
    float4 fragAdd(v2f i) : SV_Target
    {
        float4 c = tex2D(_MainTex, i.uv);
        return float4(c.rgb + tex2D(_Bloom, i.uv).rgb * _Intensity, c.a);
    }
    ENDCG
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass { CGPROGRAM #pragma vertex vert #pragma fragment fragThreshold ENDCG }
        Pass { CGPROGRAM #pragma vertex vert #pragma fragment fragBlur ENDCG }
        Pass { CGPROGRAM #pragma vertex vert #pragma fragment fragAdd ENDCG }
    }
    Fallback Off
}
