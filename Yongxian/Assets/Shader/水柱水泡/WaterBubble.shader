// 水泡粒子 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 用途：气泡柱（鱼缸增氧泵那种一串串上浮的小气泡）里的「单个水泡」外观。
//       做成材质后挂到 Particle System 的 Renderer 上，每颗粒子画成一颗水泡。
//
// 每颗水泡由三部分构成：
//   1. 泡身   —— 中心几乎透明、带一点淡淡的水色，能透出背景
//   2. 泡缘   —— 一圈偏亮的折射边：用 UV 假装球面法线 + 朝边缘的菲涅尔算出来
//   3. 高光点 —— 两枚固定反光点（一大一小），模拟天光在水泡表面的反射
//
// 特性：
//   1. 程序化正圆：不需要贴图，靠 UV 到中心的距离算圆形遮罩（和 Unlit/PlanetGlow 同思路）
//   2. 表面晃动：_Wobble 让轮廓轻微不规则，像表面张力下的软球（0 = 关闭，完全正圆）
//   3. 粒子染色：材质 _Color × 粒子顶点色 → Start Color / Color over Lifetime 直接生效
//   4. 软粒子：与场景深度相交处淡出（需 Quality 设置勾选 Soft Particles 且相机有深度纹理）
//   5. 可选贴图：_MainTex 留白就是纯程序化水泡，想叠细节时再填
Shader "Unlit/WaterBubble"
{
    Properties
    {
        _MainTex ("贴图（留白 = 纯程序化水泡，可选）", 2D) = "white" {}
        _Color ("水泡染色", Color) = (0.72, 0.9, 1, 1)
        _Opacity ("整体不透明度", Range(0, 1)) = 0.8

        _BodyAlpha ("泡身透明度（中心的水体感）", Range(0, 1)) = 0.12
        _RimColor ("泡缘颜色", Color) = (1, 1, 1, 1)
        _RimStrength ("泡缘亮度", Range(0, 4)) = 1.5
        _RimPower ("泡缘宽度（越大越细）", Range(0.5, 8)) = 2

        _GlintStrength ("高光点亮度", Range(0, 4)) = 1
        _GlintSize ("高光点大小", Range(0.02, 0.5)) = 0.1
        _GlintSharp ("高光点锐度（越大越小越实）", Range(1, 30)) = 6

        _Wobble ("表面晃动（0 = 正圆）", Range(0, 0.25)) = 0.03
        _WobbleSpeed ("晃动速度", Range(0, 10)) = 3

        _EdgeSoft ("边缘柔和度", Range(0.005, 0.3)) = 0.06
        _InvFade ("软粒子边缘（与场景相交处淡出）", Range(0.01, 10)) = 2

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("面剔除（0 = 双面）", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" "PreviewType" = "Plane" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "Always" }   // 不受场景灯光影响，始终渲染

            Blend SrcAlpha OneMinusSrcAlpha   // 普通半透明：水泡透出背景
            ZWrite Off
            Cull [_Cull]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_particles   // 让 SOFT_PARTICLES_ON 生效（Quality 里开 Soft Particles 才启用）

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Opacity;
            float _BodyAlpha;
            fixed4 _RimColor;
            float _RimStrength;
            float _RimPower;
            float _GlintStrength;
            float _GlintSize;
            float _GlintSharp;
            float _Wobble;
            float _WobbleSpeed;
            float _EdgeSoft;
            float _InvFade;

            #ifdef SOFTPARTICLES_ON
            sampler2D _CameraDepthTexture;
            #endif

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;         // 粒子系统 / 顶点色染色
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            #ifdef SOFTPARTICLES_ON
                float4 projPos : TEXCOORD1;
            #endif
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
            #ifdef SOFTPARTICLES_ON
                o.projPos = ComputeScreenPos(o.pos);
                COMPUTE_EYEDEPTH(o.projPos.z);
            #endif
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;

                // ---------- 表面晃动：让轮廓不是死正圆（_Wobble = 0 时完全关闭） ----------
                float2 wob = float2(sin(uv.y * 12.566 + t * _WobbleSpeed),
                                    cos(uv.x * 12.566 - t * _WobbleSpeed * 0.83)) * _Wobble;
                float2 p = (uv + wob) * 2.0 - 1.0;   // 面片中心 = (0,0)，边缘 = ±1

                float r2 = dot(p, p);
                float r = sqrt(r2);

                // 圆形遮罩：r < 1 - _EdgeSoft 内为 1，到 r = 1 平滑衰减为 0（外面不会露出方形边）
                float circle = 1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, r);

                // ---------- 假球面法线：中心朝向相机（0,0,1），越靠边越翘向侧面 ----------
                // 粒子面片始终朝向相机（Billboard），所以直接用 (0,0,1) 当视线方向即可
                float nz = sqrt(saturate(1.0 - r2));

                // 泡缘：视线越掠射越亮（1 - nz），形成水泡边缘的一圈亮边
                float fres = pow(saturate(1.0 - nz), _RimPower);

                // ---------- 两枚高光点：主高光偏左上、次高光偏右下 ----------
                float g1 = 1.0 - length(uv - float2(0.36, 0.66)) / _GlintSize;
                float g2 = 1.0 - length(uv - float2(0.62, 0.34)) / (_GlintSize * 1.6);
                float glint = pow(saturate(g1), _GlintSharp) + pow(saturate(g2), _GlintSharp) * 0.5;

                // ---------- 合成 ----------
                fixed3 tint = _Color.rgb * i.color.rgb;      // 材质色 × 粒子色（Start Color / Color over Lifetime）
                float3 highlight = _RimColor.rgb * (fres * _RimStrength + glint * _GlintStrength);

                fixed4 tex = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex));
                float3 col = tint * (_BodyAlpha + highlight) * tex.rgb;

                float alpha = saturate(_BodyAlpha + fres * _RimStrength + glint * _GlintStrength);
                alpha *= circle * _Opacity * i.color.a * tex.a;

            #ifdef SOFTPARTICLES_ON
                // 软粒子：与场景深度相交处淡出，避免水泡贴到鱼身/玻璃时被硬切出直边
                float sceneZ = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.projPos)));
                float fade = saturate(_InvFade * (sceneZ - i.projPos.z));
                alpha *= fade;
            #endif

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}