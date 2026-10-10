shader_type canvas_item;
render_mode blend_add, unshaded;

// 稀有度雾（2026-10-10，由 rareitem_blowfog 原型改写）：
// mode 0＝条目雾：雾从左缘涌出、被风吹向右侧拉成丝，wind 越大风越急、丝越长、吹得越远；
// mode 1＝详情页底雾：自下而上缓缓飘升，左右轻摆，不受 wind 影响速度。
// clip 把被视口裁掉的那部分还原回整条的 UV（xy＝偏移，zw＝缩放），滚动裁切时花纹不跳。
uniform vec4 fog_color : source_color = vec4(0.4, 0.6, 1.0, 1.0);
uniform float wind : hint_range(0.0, 1.0) = 0.3;
uniform int mode = 0;
uniform float intensity : hint_range(0.0, 1.0) = 0.42;
uniform vec2 rect_size = vec2(900.0, 120.0);
uniform vec4 clip = vec4(0.0, 0.0, 1.0, 1.0);
uniform float seed = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float fbm(vec2 p) {
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++) {
        v += a * noise(p);
        p = p * 2.03 + vec2(17.3, 9.1);
        a *= 0.5;
    }
    return v;
}

void fragment() {
    vec2 uv = clip.xy + UV * clip.zw;
    // 以约 110px 为一格噪声，长条与方框都不被拉伸。
    vec2 p = uv * rect_size / 110.0 + vec2(seed * 7.13, seed * 3.71);
    float t = TIME;
    float a;
    if (mode == 0) {
        float speed = 0.35 + wind * 2.2;
        float stretch = 1.0 + wind * 3.0;
        vec2 q = vec2(p.x / stretch - t * speed, p.y);
        // 域扭曲：风里卷着的涡。
        vec2 warp = vec2(fbm(q * 0.8 + vec2(0.0, t * 0.3)), fbm(q * 0.8 + vec2(5.2, -t * 0.25)));
        float body = fbm(q + warp * 1.4);
        // 顺风细丝：横向高频、纵向收窄。
        float strands = fbm(vec2(q.x * 1.6, p.y * 3.4) + warp * 0.6);
        float fog = smoothstep(0.34, 0.8, body) * 0.85 + smoothstep(0.52, 0.9, strands) * (0.35 + wind * 0.5);
        // 源头在左缘：风越大，雾被推得越远。
        float reach = mix(0.45, 1.25, wind);
        float source = exp(-uv.x / reach * 1.6) * 0.75 + 0.25 * smoothstep(1.0, 0.2, uv.x / reach);
        float vertical = smoothstep(0.0, 0.3, uv.y) * smoothstep(1.0, 0.7, uv.y);
        float leftEdge = smoothstep(0.0, 0.04, uv.x);
        a = fog * source * vertical * leftEdge;
    } else {
        vec2 q = vec2(p.x + sin(t * 0.35 + p.y * 0.6) * 0.25, p.y + t * 0.16);
        vec2 warp = vec2(fbm(q * 0.6 + vec2(t * 0.05, 0.0)), fbm(q * 0.6 + vec2(3.7, t * 0.04)));
        float body = fbm(q * 0.9 + warp * 1.2);
        float fog = smoothstep(0.4, 0.88, body);
        // 底部浓、往上渐散；左右收边。
        float rise = mix(0.25, 1.0, smoothstep(0.0, 1.0, uv.y));
        float sides = smoothstep(0.0, 0.12, uv.x) * smoothstep(1.0, 0.88, uv.x);
        float ends = smoothstep(0.0, 0.08, uv.y) * smoothstep(1.0, 0.94, uv.y);
        a = fog * rise * sides * ends;
    }
    a = clamp(a * intensity, 0.0, 1.0);
    COLOR = vec4(fog_color.rgb, a);
}
