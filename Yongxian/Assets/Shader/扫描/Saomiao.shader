// 扫描球 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 用法：把这个材质挂到一个 Sphere 上，球扩大（缩放）时，
//       球面和场景里所有物体表面的交界处会扫出一圈发光轮廓线。
//
// 原理：
//   1. 采样相机深度纹理，重建屏幕上每个像素对应的「场景世界坐标」
//   2. 计算该点到球心的距离，与球半径比较：
//      |距离 - 半径| 很小 = 这一点正好在球面上 → 画发光线
//   3. 球心、半径直接从材质所在物体的 Transform 推导，不需要脚本传参
//      （球体用 Unity 自带 Sphere 网格，网格半径 0.5，所以 世界半径 = 0.5 × Scale）
//   4. 需要相机开启深度纹理（DepthTextureMode.Depth），配套的 ScanSphere 脚本会自动开启
//
// 注意：深度重建是按透视相机推导的，正交相机下无效。
Shader "Unlit/Saomiao"
{
    Properties
    {
        _ScanColor ("扫描线颜色", Color) = (0, 1, 1, 1)
        _ScanWidth ("扫描线宽度（世界单位）", Range(0.001, 2)) = 0.3
        _ScanIntensity ("扫描线亮度", Range(0, 20)) = 4

        _BubbleColor ("球面颜色", Color) = (0, 0.5, 1, 1)
        _BubbleIntensity ("球面亮度（0 = 球体完全隐形，只留扫描线）", Range(0, 5)) = 0.25
        _FresnelPower ("球面边缘收窄", Range(0.5, 16)) = 4

        _MeshRadius ("球网格半径（Unity Sphere = 0.5）", Float) = 0.5
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("深度测试（8=Always 隔墙可见 / 4=LessEqual 会被遮挡）", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }

        Pass
        {
            Name "ScanSphere"
            Blend One One        // 加色混合：只发光，不遮挡背景
            ZWrite Off
            Cull Off             // 双面渲染，相机在球内部时也能看到扫描线
            ZTest [_ZTest]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _CameraDepthTexture;

            fixed4 _ScanColor;
            float _ScanWidth;
            float _ScanIntensity;
            fixed4 _BubbleColor;
            float _BubbleIntensity;
            float _FresnelPower;
            float _MeshRadius;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.pos);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            // 从深度纹理重建场景世界坐标（透视相机）
            float3 SceneWorldPos (float2 uv)
            {
                float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float eyeDepth = LinearEyeDepth(rawDepth);          // 沿相机前向的线性深度
                float2 ndc = uv * 2.0 - 1.0;
                // 视空间位置：z = -eyeDepth，x/y 按视锥张角还原
                float3 viewPos = float3(ndc.x / unity_CameraProjection._m00,
                                        ndc.y / unity_CameraProjection._m11,
                                        -1.0) * eyeDepth;
                return mul(unity_CameraToWorld, float4(viewPos, 1.0)).xyz;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---------- 球心与半径（从自身 Transform 推导）----------
                float3 centerWS = unity_ObjectToWorld._m03_m13_m23;
                float scaleMax = max(length(unity_ObjectToWorld._m00_m10_m20),
                                 max(length(unity_ObjectToWorld._m01_m11_m21),
                                     length(unity_ObjectToWorld._m02_m12_m22)));
                float radius = _MeshRadius * scaleMax;

                // ---------- 扫描线 ----------
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float3 sceneWS = SceneWorldPos(uv);
                float d = distance(sceneWS, centerWS) - radius;     // 相对球面的有符号距离
                float scanLine = 1.0 - smoothstep(0.0, _ScanWidth, abs(d));

                // ---------- 球面边缘光（可选，_BubbleIntensity = 0 时球体隐形）----------
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float rim = pow(1.0 - saturate(abs(dot(normalize(i.worldNormal), viewDir))), _FresnelPower);

                float3 col = _ScanColor.rgb * (scanLine * _ScanIntensity)
                           + _BubbleColor.rgb * (rim * _BubbleIntensity);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}