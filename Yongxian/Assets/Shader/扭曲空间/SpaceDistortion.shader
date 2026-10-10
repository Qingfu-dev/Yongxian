// 扭曲空间 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 用途：给爆炸后留在中心的"奇点"做空间扭曲 —— 不是画一个黑洞，而是把背后的
//       星空与星球整体"掰弯"：靠近中心被吸拢、拧出旋涡，边缘平滑过渡回原样，
//       方片本身完全不可见（看不到的物体，只看到被扭弯的空间）。
//
// 用法：
//   1. 新建材质，Shader 选 Unlit/SpaceDistortion，挂到一个 Quad 上
//      （不要用球体：遮罩是按方片 UV 从中心径向计算的）
//   2. 方片缩放 = 扭曲影响的范围（Quad 默认 1×1 米，Scale 直接按米填）
//   3. 不用管朝向：Shader 里做了广告牌，永远面向相机；建议放非拖尾层（Layer 0）
//
// 原理（GrabPass 截屏折射）：
//   先把"当前已经画好的画面"（星空 + 星球）抓成一张贴图，再按「屏幕位置到中心的
//   距离」对贴图做径向缩放（_Pull）与旋转（_Swirl），输出被掰弯后的背景。
//   扭曲量在方片边缘衰减到 0 → 接缝不可见；_Strength = 0 时完全等同原画面。
//
// 说明：
//   1. 透明队列 + ZWrite Off：只替换背景，被更近的物体挡住的部分不画
//   2. GrabPass 是每帧一次全屏抓取（同一帧多个同类物体也只抓一次），场景里放一两个没问题
//   3. _CoreRadius > 0 时中心会压出一个纯黑"事件视界"，默认 0 = 只扭曲、不画黑核
Shader "Unlit/SpaceDistortion"
{
    Properties
    {
        _Strength ("总强度（0 = 完全无扭曲）", Range(0, 3)) = 1

        _Pull ("径向拉伸（正 = 向中心吸拢，负 = 向外推）", Range(-0.9, 0.9)) = 0.3
        _Falloff ("边缘衰减（越大扭曲越集中在中部）", Range(0.3, 6)) = 2

        _Swirl ("旋涡角度（度）", Range(-180, 180)) = 30
        _SwirlSpeed ("旋涡自转速度（度/秒）", Range(-180, 180)) = 6

        _CoreRadius ("可选黑核半径（0 = 纯扭曲，0~1 = 方片比例）", Range(0, 1)) = 0
        _CoreColor ("黑核颜色", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        LOD 100

        GrabPass { "_SpaceGrab" }   // 截取当前画面（GrabPass 是 SubShader 级命令，必须放在 Pass 外面）

        Pass
        {
            Tags { "LightMode" = "Always" }

            Blend Off        // 直接用"掰弯后的背景"替换当前像素
            ZWrite Off
            ZTest LEqual     // 被更近的物体挡住的部分不画
            Cull Off

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _SpaceGrab;

            float _Strength;
            float _Pull;
            float _Falloff;
            float _Swirl;
            float _SwirlSpeed;
            float _CoreRadius;
            fixed4 _CoreColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;          // 方片自身 UV：算遮罩用
                float4 screenPos : TEXCOORD1;   // 当前像素的屏幕坐标
                float2 centerUV : TEXCOORD2;    // 物体中心投影到屏幕的位置
                float visible : TEXCOORD3;      // 中心在相机前方才显示
            };

            v2f vert (appdata v)
            {
                v2f o;

                // ---- 广告牌：忽略物体自身旋转、永远面向相机，保留物体缩放 ----
                float3 worldCenter = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float3 camRight = UNITY_MATRIX_V._m00_m10_m20;
                float3 camUp = UNITY_MATRIX_V._m01_m11_m21;
                float3 scale = float3(
                    length(unity_ObjectToWorld._m00_m01_m02),
                    length(unity_ObjectToWorld._m10_m11_m12),
                    length(unity_ObjectToWorld._m20_m21_m22));

                float3 worldPos = worldCenter
                                + camRight * (v.vertex.x * scale.x)
                                + camUp * (v.vertex.y * scale.y);

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.pos);

                // 物体中心投影到屏幕（和 screenPos 用同一套坐标约定，直接相减即可）
                float4 centerClip = mul(UNITY_MATRIX_VP, float4(worldCenter, 1.0));
                o.centerUV = ComputeScreenPos(centerClip).xy / centerClip.w;
                o.visible = centerClip.w > 0 ? 1.0 : 0.0;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                clip(i.visible - 0.5);   // 物体中心跑到相机背后就不显示

                float2 screenUV = i.screenPos.xy / i.screenPos.w;

                // 方片内的归一化半径：0 = 中心，1 = 内切圆边缘（再往外扭曲为 0 → 无缝）
                float r = saturate(length(i.uv - 0.5) * 2.0);
                float falloff = pow(saturate(1.0 - r), _Falloff);

                // 从屏幕中心出发的向量：径向缩放 + 随衰减减弱的旋转（内圈转得多 = 旋涡）
                float2 p = screenUV - i.centerUV;
                float angle = radians(_Swirl + _Time.y * _SwirlSpeed) * falloff * _Strength;
                float c = cos(angle);
                float s = sin(angle);
                float2 pr = float2(p.x * c - p.y * s, p.x * s + p.y * c);

                float2 sampleUV = saturate(i.centerUV + pr * (1.0 + _Pull * falloff * _Strength));

                float3 col = tex2D(_SpaceGrab, sampleUV).rgb;

                // 可选黑核（事件视界）：_CoreRadius = 0 时不生效，保持"纯扭曲"
                if (_CoreRadius > 0.0001)
                {
                    float core = 1.0 - smoothstep(_CoreRadius * 0.75, _CoreRadius, r);
                    col = lerp(col, _CoreColor.rgb, core);
                }

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
