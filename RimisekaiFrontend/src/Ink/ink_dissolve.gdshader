shader_type canvas_item;

// 泼墨消融：随机大小的黑色墨团泼散覆盖界面，白色笔画被墨痕盖没即隐。
// progress 0 = 完整，1 = 全幅化尽。只染黑本层画着的被消融内容，
// 空黑处的墨点不可见，故未消融的区域（净头像、按钮等）不受影响。
uniform float progress : hint_range(0.0, 1.0) = 0.0;

float hash21(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

// 一层泼点：每格随机一颗墨团——随机位置、随机大小、随机出场时序，边缘随角度抖出泼溅毛边。
float splat_layer(vec2 uv, float cells, float t, float seed, float rmin, float rmax)
{
    float cover = 0.0;
    vec2 g = uv * cells;
    vec2 id = floor(g);
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            vec2 cid = id + vec2(float(dx), float(dy));
            float h = hash21(cid * 1.37 + seed);
            if (h > t)
                continue; // 还没轮到它出场
            vec2 c = vec2(hash21(cid + seed + 1.7), hash21(cid + seed + 3.9));
            float r = rmin + (rmax - rmin) * hash21(cid + seed + 5.1);
            vec2 rel = fract(g) - vec2(float(dx), float(dy)) - c;
            float d = length(rel);
            float ang = atan(rel.y, rel.x);
            r *= 0.82 + 0.28 * hash21(vec2(floor((ang + 3.1416) * 5.0), cid.x * 3.1 + cid.y));
            cover = max(cover, 1.0 - smoothstep(r * 0.7, r, d));
        }
    }
    return cover;
}

void fragment()
{
    // 屏幕纵横比校正，让墨团在屏上是圆的。
    vec2 uv = SCREEN_UV * vec2(1.778, 1.0);
    float t = progress * 1.18;

    float cover = 0.0;
    cover = max(cover, splat_layer(uv, 7.0, t, 11.0, 0.26, 0.60));            // 大墨团
    cover = max(cover, splat_layer(uv, 13.0, min(t * 1.06, 1.0), 31.0, 0.14, 0.40)); // 中点
    cover = max(cover, splat_layer(uv, 25.0, min(t * 1.12, 1.0), 57.0, 0.05, 0.18)); // 飞沫

    // 收尾一截整幅压黑，保证化尽无残留。
    cover = max(cover, smoothstep(0.9, 1.0, progress));
    cover = clamp(cover, 0.0, 1.0);

    vec4 c = COLOR;
    c.rgb = mix(c.rgb, vec3(0.0), cover);
    COLOR = c;
}
