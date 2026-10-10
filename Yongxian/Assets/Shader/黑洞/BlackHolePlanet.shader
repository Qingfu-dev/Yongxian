// 黑洞星球 Shader（Built-in Render Pipeline / Unity 2022.3）
//
// 用途：把一个球体渲染成一个黑洞 —— 中心是吸收一切的事件视界（纯黑），
//       赤道方向有一圈自转的吸积盘（越靠内越亮越白，越外越暗越红），
//       视界边缘压出一圈明亮的光子环，轮廓外还有一层柔和外发光（用来近似引力透镜的亮边）。
//
// 用法：
//   1. 新建材质，Shader 选 Unlit/BlackHolePlanet，挂到一个 Sphere 上
//      （必须用球体：环带按球面纬度、事件视界按球面轮廓半径计算）
//   2. 球体缩放 = 黑洞整体大小；吸积盘的方向跟着物体的本地 Y 轴走，旋转物体即可倾斜盘面
//   3. 参数都在材质里可调（见下方分组注释），想全关发光把对应强度设 0 即可
//
// 原理：
//   事件视界：用 N·V 反推屏幕上的径向位置 r（0 = 正中心，1 = 轮廓边缘），
//             r 小于「事件视界半径」的像素直接输出纯黑。
//   吸积盘：  取物体空间法线的 |y| 作为纬度，赤道附近划出一条环带；
//             环带内按「到视界的距离」决定冷暖与亮度（内热外冷），
//             再用 atan2 得到的方位角做旋转与湍流扰动，让它转起来。
//   光子环：  在 r ≈ 事件视界半径 处用一条高斯亮环提亮。
//   外发光：  第二个加色 Pass，沿法线把球体略微放大后用 Cull Front 画出轮廓外的光晕。
//
// 说明：
//   1. 主 Pass 不透明（ZWrite On）：黑洞会正常遮挡身后的星空与星球
//   2. 输出的亮度可以超过 1，开了 Bloom 后盘面与光子环会自然发光
//   3. 外发光用「法线外扩」近似，扩张过大时轮廓会显得偏胖，_GlowExpand 保持 0.05~0.2 比较自然
Shader "Unlit/BlackHolePlanet"
{
    Properties
    {
        // ---- 事件视界（中心的黑核）----
        _HorizonRadius ("事件视界半径（占球体屏幕半径的比例，0~1）", Range(0, 0.99)) = 0.5
        _HorizonSoftness ("视界边缘过渡宽度（越小边缘越硬）", Range(0.001, 0.5)) = 0.05

        // ---- 光子环（贴着视界边缘的亮环）----
        _PhotonRingColor ("光子环颜色", Color) = (1, 0.85, 0.55, 1)
        _PhotonRingIntensity ("光子环亮度", Range(0, 12)) = 4
        _PhotonRingWidth ("光子环宽度", Range(0.005, 0.5)) = 0.07

        // ---- 吸积盘（赤道上的环带）----
        _DiskColorHot ("吸积盘内圈（高温）颜色", Color) = (1, 0.95, 0.82, 1)
        _DiskColorCool ("吸积盘外圈（低温）颜色", Color) = (1, 0.32, 0.05, 1)
        _DiskIntensity ("吸积盘亮度", Range(0, 12)) = 3.5
        _DiskWidth ("吸积盘宽度（赤道上下各占多少纬度，0~1）", Range(0.02, 1)) = 0.4
        _DiskThickness ("吸积盘边缘柔化（越大边缘越虚）", Range(0.01, 1)) = 0.4
        _DiskSpeed ("吸积盘旋转速度（度/秒的近似值，负 = 反向）", Range(-5, 5)) = 0.7
        _DiskTurbulence ("吸积盘湍流强度（0 = 均匀，1 = 明暗分明）", Range(0, 1)) = 0.6
        _DiskTurbulenceScale ("吸积盘湍流密度", Range(1, 40)) = 14

        // ---- 外发光（近似引力透镜的亮边）----
        _GlowColor ("外发光颜色", Color) = (1, 0.8, 0.45, 1)
        _GlowStrength ("外发光强度（0 = 关闭）", Range(0, 5)) = 1
        _GlowPower ("外发光收紧程度（越大越贴轮廓）", Range(0.5, 12)) = 4
        _GlowExpand ("外发光外扩量（沿法线放大的比例）", Range(0, 0.5)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 100

        // ================= 主 Pass：事件视界 + 吸积盘 + 光子环 =================
        Pass
        {
            Tags { "LightMode" = "Always" }

            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float _HorizonRadius;
            float _HorizonSoftness;

            fixed4 _PhotonRingColor;
            float _PhotonRingIntensity;
            float _PhotonRingWidth;

            fixed4 _DiskColorHot;
            fixed4 _DiskColorCool;
            float _DiskIntensity;
            float _DiskWidth;
            float _DiskThickness;
            float _DiskSpeed;
            float _DiskTurbulence;
            float _DiskTurbulenceScale;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;   // 世界法线
                float3 viewDir : TEXCOORD1;       // 指向相机的世界方向
                float3 objDir : TEXCOORD2;        // 物体空间位置方向（球面上 = 单位法线）
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                o.objDir = normalize(v.vertex.xyz);   // 球体：顶点方向就是法线，按纬度算环带
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.objDir);
                float3 wn = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);

                // 屏幕上的径向位置：0 = 球心方向，1 = 轮廓边缘（球体近似）
                float ndv = saturate(dot(wn, v));
                float r = sqrt(saturate(1.0 - ndv * ndv));

                // ---- 事件视界：r 小于视界半径的部分纯黑，外面才看得到东西 ----
                float outside = smoothstep(_HorizonRadius, _HorizonRadius + _HorizonSoftness, r);

                // ---- 吸积盘：赤道附近的一条环带 ----
                float latitude = abs(n.y);
                float band = 1.0 - smoothstep(_DiskWidth * (1.0 - _DiskThickness), _DiskWidth, latitude);
                band *= smoothstep(_HorizonRadius * 0.7, _HorizonRadius * 1.05, r);   // 贴着视界才最亮

                // 方位角 + 时间 = 自转；再叠一层正弦湍流做出明暗条纹
                float azimuth = atan2(n.z, n.x) + _Time.y * _DiskSpeed;
                float turbulence = 0.5 + 0.5 * sin(azimuth * _DiskTurbulenceScale + r * 6.0);
                turbulence = lerp(1.0, turbulence, _DiskTurbulence);

                // 内圈更热更亮：从视界半径到轮廓边缘，热度 1 → 0
                float heat = saturate(1.0 - (r - _HorizonRadius) / max(0.0001, 1.0 - _HorizonRadius));
                float3 diskColor = lerp(_DiskColorCool.rgb, _DiskColorHot.rgb, heat);
                float3 disk = diskColor * (band * turbulence * (0.35 + 0.65 * heat) * _DiskIntensity);

                // ---- 光子环：在 r ≈ 视界半径 处压一条高斯亮环 ----
                // 用 d*d 而不是 pow(d, 2)：d 可能为负，pow 对负底数在 D3D 上会出问题
                float ringOffset = (r - _HorizonRadius) / max(0.0001, _PhotonRingWidth);
                float ring = exp(-ringOffset * ringOffset);
                float3 photon = _PhotonRingColor.rgb * (ring * _PhotonRingIntensity);

                // 吸积盘的近侧会挡在黑洞前面（远侧被吸进去，看不见）
                float3 objViewDir = normalize(mul((float3x3)unity_WorldToObject, v));
                float nearSide = step(0.0, dot(n, objViewDir));
                float visible = max(outside, nearSide * step(0.001, band));

                float3 color = (disk + photon) * visible;

                return fixed4(color, 1.0);
            }
            ENDCG
        }

        // ================= 外发光 Pass：轮廓外的柔和亮边（近似引力透镜）=================
        Pass
        {
            Tags { "LightMode" = "Always" }

            Blend SrcAlpha One     // 加色叠加
            ZWrite Off
            ZTest LEqual
            Cull Front             // 只画放大的背面，形成轮廓外的光晕

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            fixed4 _GlowColor;
            float _GlowStrength;
            float _GlowPower;
            float _GlowExpand;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                // 沿法线把球体放大一点，让光晕露在轮廓外
                float3 expanded = v.vertex.xyz + v.normal * _GlowExpand;
                o.pos = UnityObjectToClipPos(float4(expanded, 1.0));
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 wn = normalize(i.worldNormal);
                float3 v = normalize(i.viewDir);

                // 越接近轮廓越亮
                float rim = pow(saturate(1.0 - saturate(dot(wn, v))), _GlowPower);
                float a = rim * _GlowStrength * _GlowColor.a;

                return fixed4(_GlowColor.rgb, a);
            }
            ENDCG
        }
    }

    Fallback Off
}
