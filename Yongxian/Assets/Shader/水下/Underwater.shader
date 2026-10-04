// 水下滤镜 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 这是一个全屏后处理 Shader，不直接挂在物体上，而是由 UnderwaterCameraEffect.cs
// 挂在相机上，通过 OnRenderImage 对整个画面做处理。
//
// 效果构成（从下往上叠加）：
//   1. 水面波动  —— 两层不同尺度/速度的正弦波扭曲 UV，模拟水面的折射晃动
//   2. 采样      —— 轻微的 RGB 色散（折射）+ 散射模糊（水中悬浮颗粒让画面发糊）
//   3. 水的吸收  —— 采样相机深度图，越远越浑浊；红光吸收最快，所以远处偏青蓝
//   4. 焦散      —— 屏幕上叠加一层缓慢流动的水面光斑
//   5. 暗角      —— 画面四周向深水色变暗，增强"被水包围"的感觉
//
// 需要相机开启深度纹理（DepthTextureMode.Depth），配套脚本会自动开启。
Shader "Hidden/Underwater"
{
    Properties
    {
        _MainTex ("画面", 2D) = "white" {}

        _WaterColor ("水色（远处景物融入的颜色）", Color) = (0.05, 0.32, 0.38, 1)
        _Density ("浑浊度（越大可见距离越短）", Range(0, 0.5)) = 0.08
        _Absorption ("三通道吸收比例（红光吸收最快 → 水下偏青蓝）", Color) = (1, 0.45, 0.25, 1)

        _DistortionStrength ("波动强度", Range(0, 5)) = 1
        _DistortionSpeed ("波动速度", Range(0, 5)) = 1
        _WaveScale ("波动密度", Range(1, 40)) = 9

        _CausticsColor ("焦散颜色", Color) = (1, 0.96, 0.82, 1)
        _CausticsIntensity ("焦散强度", Range(0, 3)) = 0.4
        _CausticsScale ("焦散大小", Range(1, 40)) = 12
        _CausticsSpeed ("焦散速度", Range(0, 3)) = 0.7

        _ScatterBlur ("散射模糊（0 = 清晰）", Range(0, 1)) = 0.35
        _ChromaticAberration ("色散（边缘 RGB 分离）", Range(0, 1)) = 0.15
        _VignetteIntensity ("暗角强度", Range(0, 1)) = 0.35
        _VignettePower ("暗角范围（越大越集中在四周）", Range(0.5, 6)) = 2.5
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _CameraDepthTexture;

            fixed4 _WaterColor;
            float _Density;
            fixed4 _Absorption;

            float _DistortionStrength;
            float _DistortionSpeed;
            float _WaveScale;

            fixed4 _CausticsColor;
            float _CausticsIntensity;
            float _CausticsScale;
            float _CausticsSpeed;

            float _ScatterBlur;
            float _ChromaticAberration;
            float _VignetteIntensity;
            float _VignettePower;

            // 焦散图案：两层正弦网格交叠后取亮线，形成水面光斑网络
            float CausticsMask(float2 p, float t)
            {
                // 先做一次扭曲，让网格不是规整的方格，更像水面波动
                float2 q = p + float2(sin(p.y * 1.7 + t * 1.1), sin(p.x * 1.4 - t * 0.9)) * 0.65;
                float v1 = sin(q.x) * sin(q.y);
                float v2 = sin(q.x * 1.87 + t * 0.63) * sin(q.y * 2.13 - t * 0.71);
                float v = v1 * 0.65 + v2 * 0.45;
                // 取接近 ±1 的窄峰 → 细亮的线条
                return pow(saturate(abs(v)), 6.0);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;

                // ---------- 1. 水面波动：两层不同尺度/速度的正弦叠加 ----------
                float2 wave;
                wave.x = sin(uv.y * _WaveScale + t * _DistortionSpeed * 1.3)
                       + 0.5 * sin(uv.y * _WaveScale * 2.7 - t * _DistortionSpeed * 0.9);
                wave.y = sin(uv.x * _WaveScale * 0.9 + t * _DistortionSpeed * 1.1)
                       + 0.5 * sin(uv.x * _WaveScale * 3.1 + t * _DistortionSpeed * 1.7);
                float2 sampleUV = saturate(uv + wave * _DistortionStrength * 0.008);

                // ---------- 2. 采样：色散（径向 RGB 分离）+ 散射模糊 ----------
                float2 caOffset = (uv - 0.5) * _ChromaticAberration * 0.012;

                half3 center;
                center.r = tex2D(_MainTex, saturate(sampleUV + caOffset)).r;
                center.g = tex2D(_MainTex, sampleUV).g;
                center.b = tex2D(_MainTex, saturate(sampleUV - caOffset)).b;

                // 四邻域平均 → 散射雾；_ScatterBlur 同时控制采样距离和混合比例
                float2 px = _MainTex_TexelSize.xy * (1.0 + _ScatterBlur * 3.0);
                half3 haze = tex2D(_MainTex, sampleUV + float2(px.x, px.y)).rgb
                           + tex2D(_MainTex, sampleUV + float2(px.x, -px.y)).rgb
                           + tex2D(_MainTex, sampleUV + float2(-px.x, px.y)).rgb
                           + tex2D(_MainTex, sampleUV + float2(-px.x, -px.y)).rgb;
                haze *= 0.25;
                half3 col = lerp(center, haze, saturate(_ScatterBlur));

                // ---------- 3. 水的吸收：越远越浑浊，红光最先消失 ----------
                // D3D 上深度图和主贴图上下方向可能相反，深度 UV 单独翻转（Unity 官方 GlobalFog 同款处理）
                float2 depthUV = sampleUV;
            #if UNITY_UV_STARTS_AT_TOP
                if (_MainTex_TexelSize.y < 0) depthUV.y = 1.0 - depthUV.y;
            #endif
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, depthUV);
                float eyeDepth = LinearEyeDepth(rawDepth);

                float3 absorb = _Density * _Absorption.rgb;     // 每米的吸收系数
                float3 trans = exp(-absorb * eyeDepth);         // 每通道的透射率，1 = 完全透明
                col = col * trans + _WaterColor.rgb * (1.0 - trans);

                // ---------- 4. 焦散：缓慢流动的水面光斑，随距离减弱 ----------
                float2 causticsUV = uv * _CausticsScale + float2(t * _CausticsSpeed * 0.35, t * _CausticsSpeed * 0.22);
                float caustics = CausticsMask(causticsUV, t * _CausticsSpeed);
                float causticsFade = exp(-_Density * eyeDepth * 0.75);
                col += _CausticsColor.rgb * (caustics * _CausticsIntensity * causticsFade);

                // ---------- 5. 暗角：四周向深水色变暗 ----------
                float vd = saturate(length(uv - 0.5) * 1.4142);   // 0 = 画面中心，1 = 四角
                float vig = saturate(_VignetteIntensity * pow(vd, max(_VignettePower, 0.01)));
                col = lerp(col, _WaterColor.rgb * 0.22, vig);

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}