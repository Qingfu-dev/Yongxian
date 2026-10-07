// 残影（视觉暂留）全屏后处理 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 配合 AfterimageCameraEffect.cs 使用：脚本会额外创建一台"残影相机"，
// 只渲染指定的层（黑底），本 Shader 负责两件事：
//
//   Pass 0  累积：把残影相机这一帧的画面按 max(历史 × 衰减, 本帧) 叠进残影缓冲
//                用 max 而不是相加，静止的物体不会把自己越叠越亮；
//                同时每帧对历史做一次很小的模糊（_Spread），把"一帧一个快照"的鬼影连成连续光带
//   Pass 1  合成：主画面 + 残影缓冲 × 发光颜色 × 强度，加性叠加回最终画面
//
// 参数都是脚本每帧写入的，这里不需要在 Inspector 上调。
Shader "Hidden/Afterimage"
{
    Properties
    {
        _MainTex ("主画面 / 历史残影缓冲", 2D) = "white" {}
        _LayerTex ("本帧该层画面（残影相机渲染）", 2D) = "black" {}
        _TrailTex ("残影缓冲", 2D) = "black" {}

        _Decay ("每帧保留比例", Range(0, 1)) = 0.9
        _CurrentWeight ("本帧画面权重", Range(0, 2)) = 1
        _FadeRush ("每秒额外削减（尾端更快消失）", Range(0, 4)) = 0
        _DeltaTime ("帧间隔", Float) = 0.016
        _Spread ("每帧扩散（像素，越大越连贯）", Range(0, 6)) = 1

        _GhostColor ("发光颜色", Color) = (0.4, 0.9, 1, 1)
        _Intensity ("发光强度", Range(0, 4)) = 1.2
        _SubtractSource ("扣除本帧画面（1 = 只保留残影）", Range(0, 1)) = 1
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // ---------- Pass 0：累积 ----------
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;      // 历史残影（上一次累积的结果）
            sampler2D _LayerTex;     // 本帧该层画面
            float4 _MainTex_TexelSize;

            float _Decay;
            float _CurrentWeight;
            float _FadeRush;
            float _DeltaTime;
            float _Spread;

            fixed4 frag (v2f_img i) : SV_Target
            {
                // 先对历史缓冲做一次很小的模糊。残影本质上是"一帧一个快照"，
                // 不模糊的话快速移动时看到的就是一个个分开的鬼影；
                // 每帧只模糊 _Spread 个像素，但会逐帧累积，最终把快照连成连续的光带。
                // （_Spread = 0 时退化成直接取中心像素，回到清晰鬼影的效果）
                float2 texel = _MainTex_TexelSize.xy * _Spread;
                half3 history = (tex2D(_MainTex, i.uv).rgb * 2.0
                               + tex2D(_MainTex, i.uv + float2(texel.x, 0.0)).rgb
                               + tex2D(_MainTex, i.uv - float2(texel.x, 0.0)).rgb
                               + tex2D(_MainTex, i.uv + float2(0.0, texel.y)).rgb
                               + tex2D(_MainTex, i.uv - float2(0.0, texel.y)).rgb) / 6.0;

                half3 current = tex2D(_LayerTex, i.uv).rgb;

                // 历史部分：按比例衰减，再减去一个固定的"快消"量（可选）
                half3 decayed = max(history * _Decay - _FadeRush * _DeltaTime, 0.0);

                // 取两者较大值 → 物体离开后留下它的"影子"，但静止的物体不会无限叠加变亮，
                // 而且当前这一帧始终保持清晰，不会被模糊糊掉
                half3 result = max(decayed, current * _CurrentWeight);

                return half4(result, 1.0);
            }
            ENDCG
        }

        // ---------- Pass 1：合成回主画面 ----------
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;      // 主画面（Graphics.Blit 自动写入）
            sampler2D _TrailTex;     // 残影缓冲

            fixed4 _GhostColor;
            float _Intensity;
            float _SubtractSource;

            fixed4 frag (v2f_img i) : SV_Target
            {
                half3 src = tex2D(_MainTex, i.uv).rgb;
                half3 trail = tex2D(_TrailTex, i.uv).rgb;

                // 只在"残影比当前画面更亮"的地方加东西：
                // 物体本体所在的位置残影 ≈ 本体，相减后约等于 0，所以本体和静止物体不会被重复提亮
                half3 extra = max(trail - src * _SubtractSource, 0.0);

                return fixed4(src + extra * _GhostColor.rgb * _Intensity, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
