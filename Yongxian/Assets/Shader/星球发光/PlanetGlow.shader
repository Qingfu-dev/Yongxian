// 圆形发光 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 用途：白色发光、可染色的圆形粒子材质，适合能量球 / 光点 / 星球光晕。
//
// 为什么不用贴图：
//   贴图默认是 1x1 纯白，整块四边形都会被填满 —— 所以之前粒子看起来是一个个白方块。
//   这里直接用 UV 到中心的距离算出「圆形遮罩」，不需要任何贴图就是正圆，
//   边缘柔和渐隐，不会出现方形边界。
//
// 特性：
//   1. 程序化圆形：圆心到边缘自然渐隐，_Radius 控制大小，_Softness 控制边缘柔和度
//   2. 径向亮度：中心最亮、向外渐弱（_Falloff），像星球/能量球
//   3. 加色混合（Additive）：只提亮背景、不遮挡，天然发光感
//   4. 染色 = 材质 _Color × 粒子顶点色，粒子系统的 Start Color / Color over Lifetime 也能直接染色
//   5. 可选贴图：想在圆里叠细节（噪点/纹理）时再填 _MainTex，留白就是纯圆
Shader "Unlit/PlanetGlow"
{
    Properties
    {
        _MainTex ("贴图（留白 = 纯圆，可选）", 2D) = "white" {}
        _Color ("染色", Color) = (1, 1, 1, 1)
        _Intensity ("发光强度", Range(0, 20)) = 3

        _Radius ("圆形大小（1 = 撑满面片）", Range(0.05, 1.5)) = 0.9
        _Softness ("边缘柔和度（越大越虚）", Range(0.001, 1)) = 0.5
        _Falloff ("中心到边缘的亮度衰减", Range(0.1, 8)) = 1.5

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("面剔除（0 = 双面）", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" "PreviewType" = "Plane" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "Always" }   // 不受场景灯光影响，始终渲染

            Blend SrcAlpha One                // 加色混合：src.rgb * src.a + 背景
            ZWrite Off
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Intensity;
            float _Radius;
            float _Softness;
            float _Falloff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;         // 粒子系统 / 顶点色染色
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 面片中心到当前像素的距离：圆心为 0，面片四边中点为 1
                float dist = length(i.uv - 0.5) * 2.0;

                // 圆形遮罩：_Radius 内核为 1，到 _Radius 处平滑衰减为 0
                float circle = 1.0 - smoothstep(_Radius - _Softness * _Radius, _Radius, dist);

                // 径向亮度：中心最亮，向外按 _Falloff 衰减
                float core = pow(saturate(1.0 - dist / max(_Radius, 1e-4)), _Falloff);

                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed3 tint = _Color.rgb * i.color.rgb;

                float3 col = tex.rgb * tint * (core * _Intensity);
                float alpha = tex.a * _Color.a * i.color.a * circle;

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}
