// 隐形 / 显形 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 用法：把这个材质挂到要隐形的物体上，平时完全看不见（_Opacity = 0），
//       被扫描线扫到时由脚本（ScanReveal）把 _Opacity 拉到 1 显形，
//       一段时间没被扫到后再缓慢淡回 0。
//
// 说明：
//   1. 兼容原来的外观：脚本可以自动把原材质的贴图 (_MainTex) 和颜色 (_Color) 复制过来
//   2. 简单光照 = 环境光 + 主平行光，不需要额外烘焙
//   3. 隐形物体不写深度、不投影，扫描线不会画在隐形物体上（只会把它"点亮显形"）
Shader "Unlit/Yingxing"
{
    Properties
    {
        _MainTex ("贴图", 2D) = "white" {}
        _BaseColor ("基础色", Color) = (1, 1, 1, 1)
        _Opacity ("不透明度（由脚本控制）", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _BaseColor;
            float _Opacity;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv) * _BaseColor;

                // 简单光照：环境光 + 主平行光
                float3 n = normalize(i.worldNormal);
                float ndotl = saturate(dot(n, normalize(_WorldSpaceLightPos0.xyz)));
                float3 lighting = UNITY_LIGHTMODEL_AMBIENT.rgb + _LightColor0.rgb * ndotl;

                return fixed4(tex.rgb * lighting, tex.a * _Opacity);
            }
            ENDCG
        }
    }
}