using System.Collections.Concurrent;
using System.Globalization;
using SkiaSharp;

namespace Froststrap.Utility
{
    /// <summary>SkSL fragment shaders for the animated backgrounds.</summary>
    public static class SceneShaders
    {
        private const string Common = """
            uniform float2 iResolution;
            uniform float iTime;
            uniform float4 uC0;
            uniform float4 uC1;
            uniform float4 uC2;
            uniform float4 uC3;
            uniform float4 uParams;
            uniform float uLight;

            float hash21(float2 p) {
                p = fract(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return fract(p.x * p.y);
            }

            float vnoise(float2 p) {
                float2 i = floor(p);
                float2 f = fract(p);
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
            }

            float fbm(float2 p) {
                float v = 0.0;
                float a = 0.5;
                float2x2 m = float2x2(1.6, 1.2, -1.2, 1.6);
                for (int i = 0; i < 5; i++) {
                    v += a * vnoise(p);
                    p = m * p;
                    a *= 0.5;
                }
                return v;
            }

            float fbm4(float2 p) {
                float v = 0.0;
                float a = 0.5;
                float2x2 m = float2x2(1.6, 1.2, -1.2, 1.6);
                for (int i = 0; i < 4; i++) {
                    v += a * vnoise(p);
                    p = m * p;
                    a *= 0.5;
                }
                return v;
            }

            float fbm3(float2 p) {
                float v = 0.0;
                float a = 0.5;
                float2x2 m = float2x2(1.6, 1.2, -1.2, 1.6);
                for (int i = 0; i < 3; i++) {
                    v += a * vnoise(p);
                    p = m * p;
                    a *= 0.5;
                }
                return v;
            }
            """;

        // Domain-warped noise lit as a glossy liquid surface.
        private const string Fluid = """
            float pattern(float2 p, float t, out float2 q, out float2 r) {
                q = float2(fbm4(p + float2(0.10, 0.04) * t),
                           fbm4(p + float2(5.2, 1.3) - float2(0.06, 0.09) * t));
                r = float2(fbm4(p + 4.0 * q + float2(1.7, 9.2) + float2(0.12, -0.05) * t),
                           fbm4(p + 4.0 * q + float2(8.3, 2.8) + float2(-0.07, 0.11) * t));
                return fbm4(p + 4.0 * r);
            }

            half4 main(float2 fc) {
                float2 uv = fc / iResolution.y;
                float t = iTime * 0.3 * uParams.y;
                float2 p = uv * uParams.x;

                float2 q; float2 r; float2 q2; float2 r2;
                float f = pattern(p, t, q, r);
                float e = 0.035;
                float fx = pattern(p + float2(e, 0.0), t, q2, r2);
                float fy = pattern(p + float2(0.0, e), t, q2, r2);
                float3 n = normalize(float3((f - fx) / e * 0.55 * uParams.z, (f - fy) / e * 0.55 * uParams.z, 1.0));
                float3 l = normalize(float3(-0.4, 0.5, 0.75));
                float diff = clamp(dot(n, l), 0.0, 1.0);
                float spec = pow(clamp(dot(n, normalize(l + float3(0.0, 0.0, 1.0))), 0.0, 1.0), 26.0);

                float3 col = mix(uC1.rgb, uC2.rgb, clamp(f * f * 3.2, 0.0, 1.0));
                col = mix(col, uC0.rgb, clamp(length(q) * 1.15 - 0.35, 0.0, 1.0));
                col = mix(col, uC3.rgb, clamp(r.x * r.x * 1.6 - 0.25, 0.0, 1.0) * 0.55 * uParams.w);

                float shade = f * f * f + 0.6 * f * f + 0.5 * f;
                float3 dark = col * shade * 1.45 * (0.7 + 0.45 * diff) + uC3.rgb * spec * 0.35 * uParams.w;
                float3 light = mix(uC0.rgb, col, 0.35 + 0.65 * clamp(shade * 1.3, 0.0, 1.0)) * (0.88 + 0.16 * diff) + spec * 0.12;
                float3 outc = mix(dark, light, uLight);

                return half4(half3(clamp(outc, 0.0, 1.0)), 1.0);
            }
            """;

        // Large satin folds with a travelling sheen.
        private const string Silk = """
            float folds(float2 p, float t) {
                float2 d = normalize(float2(0.8, -0.6));
                float s = dot(p, d);
                float u = dot(p, float2(-d.y, d.x));
                float warp = 2.2 * fbm3(p * 0.22 + float2(t * 0.025, -t * 0.02)) + 0.9 * sin(u * 0.35 + t * 0.14);
                float h = sin(s * 1.9 + warp + t * 0.08);
                h += 0.35 * sin(s * 3.7 + warp * 1.6 - t * 0.05 + 1.3);
                return h;
            }

            half4 main(float2 fc) {
                float2 uv = fc / iResolution.y;
                float t = iTime * 0.6 * uParams.y;
                float2 p = uv * 3.0 * uParams.x;

                float e = 0.015;
                float v = folds(p, t);
                float vx = folds(p + float2(e, 0.0), t);
                float vy = folds(p + float2(0.0, e), t);
                float3 n = normalize(float3((v - vx) / e * 0.9, (v - vy) / e * 0.9, 1.0));
                float3 l = normalize(float3(-0.35, -0.55, 0.75));
                float diff = clamp(dot(n, l), 0.0, 1.0);
                float spec = pow(clamp(dot(n, normalize(l + float3(0.0, 0.0, 1.0))), 0.0, 1.0), 30.0);
                float broad = pow(clamp(dot(n, normalize(l + float3(0.0, 0.0, 1.0))), 0.0, 1.0), 6.0);

                float3 col = mix(uC0.rgb, uC1.rgb, 0.35 + 0.65 * diff);
                col += uC2.rgb * broad * 0.45;
                col += uC3.rgb * spec * 0.6 * uParams.w;
                float vign = smoothstep(0.2, 1.5, length(uv - float2(0.2, 0.85)));
                col = mix(col, uC0.rgb, vign * 0.35);

                return half4(half3(clamp(col, 0.0, 1.0)), 1.0);
            }
            """;

        // Layered gas clouds.
        private const string Nebula = """
            half4 main(float2 fc) {
                float2 uv = fc / iResolution.y;
                float t = iTime * 0.1 * uParams.y;
                float2 p = uv * uParams.x;

                float2 w = float2(fbm(p + float2(t, 0.0)), fbm(p + float2(3.1, 7.4) - float2(0.0, t)));
                float2 w2 = float2(fbm(p + 3.0 * w + float2(1.7, 9.2) + t * 0.5), fbm(p + 3.0 * w + float2(8.3, 2.8) - t * 0.4));
                float f = fbm(p + 1.7 * w2);
                float mask = smoothstep(0.15, 0.75, fbm4(p * 0.5 + float2(7.7, 1.3) + float2(t * 0.3, 0.0)));

                float3 col = uC0.rgb;
                col = mix(col, uC1.rgb, clamp(f * f * 2.6, 0.0, 1.0) * (0.45 + 0.55 * mask));
                col = mix(col, uC2.rgb, smoothstep(0.45, 0.85, f) * mask * 0.8 * uParams.w);
                col += uC3.rgb * pow(clamp(f * mask * 1.2, 0.0, 1.0), 5.0) * 0.6 * uParams.w;
                col = mix(col, uC0.rgb, clamp(length(w2) - 0.75, 0.0, 1.0) * 0.6);

                return half4(half3(clamp(col, 0.0, 1.0)), 1.0);
            }
            """;

        // Aurora curtains with vertical rays.
        private const string Aurora = """
            float3 band(float2 uv, float t, float base, float amp, float seed, float3 green, float3 violet) {
                float x = uv.x;
                float wave = base + amp * sin(x * 2.3 + t * 0.28 + seed)
                           + amp * 0.5 * sin(x * 5.1 - t * 0.21 + seed * 2.0)
                           + 0.07 * (fbm3(float2(x * 1.2 + seed, t * 0.1)) - 0.5);
                float d = uv.y - wave;

                float streak = fbm3(float2(x * 22.0 + seed + t * 0.15, uv.y * 1.5 - t * 0.05));
                streak = 0.45 + 0.9 * streak * streak;
                float pulse = 0.75 + 0.25 * fbm3(float2(x * 3.0 - t * 0.3, seed));

                float core = exp(-d * d * 140.0);
                float tail = exp(min(d, 0.0) * 6.0) * smoothstep(0.04, -0.03, d) * 0.3;
                float above = exp(-(d + 0.12) * (d + 0.12) * 110.0);
                float below = exp(-(d - 0.075) * (d - 0.075) * 160.0);

                float3 c = green * (core + tail) * streak * pulse * 1.15;
                c += violet * (above * 0.4 + below * 0.5) * streak * pulse;
                return c;
            }

            half4 main(float2 fc) {
                float2 uv = fc / iResolution;
                uv.x *= iResolution.x / iResolution.y;
                float t = iTime * uParams.y;

                float3 col = mix(uC1.rgb, uC0.rgb, smoothstep(0.0, 1.0, uv.y));
                col += band(uv, t, 0.56, 0.10, 0.0, uC2.rgb, uC3.rgb);
                col += band(uv + float2(0.61, 0.0), t * 0.8, 0.36, 0.07, 3.7, uC2.rgb * 0.35, uC3.rgb * 0.45);

                return half4(half3(clamp(col, 0.0, 1.0)), 1.0);
            }
            """;

        // Metaball wax in a glowing lamp.
        private const string Lava = """
            float field(float2 uv, float aspect, float t) {
                float f = 0.0;
                for (int i = 0; i < 9; i++) {
                    float fi = float(i);
                    float2 c = float2(aspect * (0.5 + 0.45 * sin(t * (0.37 + fi * 0.07) + fi * 1.9)),
                                      0.5 + 0.62 * sin(t * (0.29 + fi * 0.05) + fi * 2.7));
                    float r = 0.13 + 0.06 * sin(fi * 3.1 + 1.0);
                    float2 d = uv - c;
                    f += r * r / (dot(d, d) + 0.0008);
                }
                return f;
            }

            half4 main(float2 fc) {
                float aspect = iResolution.x / iResolution.y;
                float2 uv = fc / iResolution.y;
                float t = iTime * 0.25 * uParams.y;

                float f = field(uv, aspect, t);
                float e = 0.004;
                float fx = field(uv + float2(e, 0.0), aspect, t);
                float fy = field(uv + float2(0.0, e), aspect, t);
                float3 n = normalize(float3((f - fx) / e * 0.004, (f - fy) / e * 0.004, 1.0));
                float diff = clamp(dot(n, normalize(float3(-0.4, -0.5, 0.75))), 0.0, 1.0);

                float3 bg = mix(uC1.rgb, uC0.rgb, uv.y);
                float3 wax = mix(uC3.rgb, uC2.rgb, clamp(uv.y, 0.0, 1.0));
                float blob = smoothstep(0.9, 1.15, f);
                float glow = clamp(f * 0.35, 0.0, 1.0);

                float3 col = bg + wax * glow * glow * 0.35;
                float rim = smoothstep(2.0, 1.0, f);
                float3 lit = wax * (0.82 + 0.3 * rim);
                col = mix(col, lit, blob);

                return half4(half3(clamp(col, 0.0, 1.0)), 1.0);
            }
            """;

        // Water caustics.
        private const string Caustics = """
            float caustic(float2 uv, float t) {
                float2 p = mod(uv * 6.28318, 6.28318) - 250.0;
                float2 i = p;
                float c = 1.0;
                float inten = 0.005;
                for (int n = 0; n < 5; n++) {
                    float tt = t * (1.0 - (3.5 / float(n + 1)));
                    i = p + float2(cos(tt - i.x) + sin(tt + i.y), sin(tt - i.y) + cos(tt + i.x));
                    c += 1.0 / length(float2(p.x / (sin(i.x + tt) / inten), p.y / (cos(i.y + tt) / inten)));
                }
                c /= 5.0;
                c = 1.17 - pow(c, 1.4);
                return pow(abs(c), 8.0);
            }

            half4 main(float2 fc) {
                float2 uv = fc / iResolution.y;
                float t = iTime * 0.35 * uParams.y + 23.0;
                float2 p = uv * 0.9 * uParams.x;
                p += 0.08 * float2(fbm3(p * 2.0 + t * 0.05), fbm3(p * 2.0 + 5.0 - t * 0.05));

                float c = caustic(p, t);
                float c2 = caustic(p * 1.7 + 0.3, t * 1.3);

                float depth = smoothstep(0.0, 1.2, fc.y / iResolution.y);
                float3 col = mix(uC1.rgb, uC0.rgb, depth);
                col += uC2.rgb * clamp(c, 0.0, 1.0) * 0.8 * uParams.w * (1.0 - depth * 0.5);
                col += uC3.rgb * clamp(c * c2 * 1.5, 0.0, 1.0) * 0.5 * uParams.w;
                col *= 0.85 + 0.25 * fbm3(p * 1.5 + t * 0.03);

                return half4(half3(clamp(col, 0.0, 1.0)), 1.0);
            }
            """;

        // Soft four-point mesh gradient.
        private const string Mesh = """
            half4 main(float2 fc) {
                float aspect = iResolution.x / iResolution.y;
                float2 uv = fc / iResolution.y;
                float t = iTime * 0.2 * uParams.y;
                uv += 0.12 * (float2(fbm3(uv * 1.6 + t * 0.4), fbm3(uv * 1.6 + 4.3 - t * 0.4)) - 0.5);

                float2 p0 = float2(aspect * (0.15 + 0.12 * sin(t * 0.9)), 0.15 + 0.12 * cos(t * 0.7));
                float2 p1 = float2(aspect * (0.85 + 0.10 * cos(t * 0.8 + 1.0)), 0.25 + 0.15 * sin(t * 0.6 + 2.0));
                float2 p2 = float2(aspect * (0.70 + 0.15 * sin(t * 0.5 + 3.0)), 0.90 + 0.10 * cos(t * 0.9 + 1.0));
                float2 p3 = float2(aspect * (0.20 + 0.14 * cos(t * 0.7 + 4.0)), 0.80 + 0.12 * sin(t * 0.8 + 2.5));

                float w0 = 1.0 / pow(dot(uv - p0, uv - p0) + 0.02, 1.6);
                float w1 = 1.0 / pow(dot(uv - p1, uv - p1) + 0.02, 1.6);
                float w2 = 1.0 / pow(dot(uv - p2, uv - p2) + 0.02, 1.6);
                float w3 = 1.0 / pow(dot(uv - p3, uv - p3) + 0.02, 1.6);

                float3 col = (uC0.rgb * w0 + uC1.rgb * w1 + uC2.rgb * w2 + uC3.rgb * w3) / (w0 + w1 + w2 + w3);
                col += (hash21(fc + fract(iTime)) - 0.5) * 0.012;

                return half4(half3(clamp(col, 0.0, 1.0)), 1.0);
            }
            """;

        private static readonly ConcurrentDictionary<SceneKind, SKRuntimeEffect?> Effects = new();

        // Recolours the ribbon mark: keeps its fold shading, fills it with a flowing scene gradient and a passing sheen.
        private const string Logo = """
            uniform shader logo;
            uniform float2 iSize;
            uniform float iTime;
            uniform float4 uS0;
            uniform float4 uS1;
            uniform float4 uS2;
            uniform float4 uS3;

            float3 ramp(float k) {
                k = fract(k);
                float3 a = uS2.rgb;
                float3 b = uS0.rgb;
                float f = (k - 0.6667) * 3.0;
                if (k < 0.3333) {
                    a = uS0.rgb; b = uS1.rgb; f = k * 3.0;
                } else if (k < 0.6667) {
                    a = uS1.rgb; b = uS2.rgb; f = (k - 0.3333) * 3.0;
                }
                return mix(a, b, smoothstep(0.0, 1.0, f));
            }

            half4 main(float2 p) {
                half4 m = logo.eval(p);
                if (m.a < 0.004) {
                    return half4(0.0);
                }

                float3 src = float3(m.rgb) / float(m.a);
                float shade = src.r / 0.745;
                float face = clamp(shade, 0.0, 1.0);
                float rim = clamp(shade - 1.0, 0.0, 0.4);

                float2 uv = p / iSize;
                float t = iTime;
                float flow = uv.x * 0.55 + uv.y * 0.85;
                float w = flow + 0.10 * sin(uv.y * 6.0 + t * 0.8) + 0.07 * sin(uv.x * 9.0 - t * 0.6);
                float3 col = ramp(w * 0.75 - t * 0.05);

                float3 fold = mix(uS0.rgb * 0.18, col * 0.35, face);
                col = mix(fold, col * (0.9 + 0.4 * face), smoothstep(0.25, 0.85, face));
                col += uS3.rgb * rim * 1.6;

                float s = flow - (fract(t * 0.09) * 2.8 - 0.6);
                float band = exp(-s * s * 45.0);
                col = mix(col, uS3.rgb, band * 0.65 * face);

                return half4(half3(clamp(col, 0.0, 1.0)) * m.a, m.a);
            }
            """;

        private static SKRuntimeEffect? _logoEffect;

        /// <summary>Builds the logo shader. <paramref name="mark"/> must already be mapped to the <paramref name="width"/> x <paramref name="height"/> area.</summary>
        public static SKShader? CreateLogoShader(SceneSpec spec, SKShader mark, float width, float height, float time)
        {
            if (_logoEffect is null)
            {
                _logoEffect = SKRuntimeEffect.CreateShader(Logo, out string errors);
                if (_logoEffect is null)
                {
                    LastError = $"Logo: {errors}";
                    return null;
                }
            }

            string[] colors = SceneCatalog.LogoColors(spec);
            var uniforms = new SKRuntimeEffectUniforms(_logoEffect)
            {
                ["iSize"] = new[] { width, height },
                ["iTime"] = time,
                ["uS0"] = ToFloats(colors[0]),
                ["uS1"] = ToFloats(colors[1]),
                ["uS2"] = ToFloats(colors[2]),
                ["uS3"] = ToFloats(colors[3]),
            };
            var children = new SKRuntimeEffectChildren(_logoEffect) { ["logo"] = mark };

            return _logoEffect.ToShader(uniforms, children);
        }

        public static string? LastError { get; private set; }

        public static string? Source(SceneKind kind) => kind switch
        {
            SceneKind.Fluid => Common + Fluid,
            SceneKind.Silk => Common + Silk,
            SceneKind.Nebula => Common + Nebula,
            SceneKind.Aurora => Common + Aurora,
            SceneKind.Lava => Common + Lava,
            SceneKind.Caustics => Common + Caustics,
            SceneKind.Mesh => Common + Mesh,
            _ => null
        };

        public static SKRuntimeEffect? GetEffect(SceneKind kind) => Effects.GetOrAdd(kind, k =>
        {
            string? source = Source(k);
            if (source is null)
                return null;

            var effect = SKRuntimeEffect.CreateShader(source, out string errors);
            if (effect is null)
                LastError = $"{k}: {errors}";
            return effect;
        });

        /// <summary>Builds the shader for <paramref name="spec"/> sized to a <paramref name="width"/> x <paramref name="height"/> pixel area.</summary>
        public static SKShader? CreateShader(SceneSpec spec, float width, float height, float time)
        {
            var effect = GetEffect(spec.Kind);
            if (effect is null)
                return null;

            var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["iResolution"] = new[] { width, height },
                ["iTime"] = time,
                ["uC0"] = ToFloats(spec.Colors[0]),
                ["uC1"] = ToFloats(spec.Colors[1]),
                ["uC2"] = ToFloats(spec.Colors[2]),
                ["uC3"] = ToFloats(spec.Colors[3]),
                ["uParams"] = new[] { spec.Scale, spec.Speed, spec.Detail, spec.Contrast },
                ["uLight"] = spec.IsLight ? 1f : 0f,
            };

            return effect.ToShader(uniforms);
        }

        private static float[] ToFloats(string hex)
        {
            string h = hex.TrimStart('#');
            int v = int.Parse(h[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return [((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f];
        }
    }
}
