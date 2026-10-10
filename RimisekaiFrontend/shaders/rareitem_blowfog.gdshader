shader_type canvas_item;

// 高稀有度物品背景：一团对应颜色的雾被风快速朝某个方向吹散，留下拉长的尾迹。
uniform vec4 color_fx : source_color = vec4(0.85, 0.55, 0.15, 1.0);
uniform float speed : hint_range(0.1, 4.0) = 1.4;
uniform vec2 direction = vec2(1.0, 0.0);

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

void fragment() {
    vec2 uv = UV;
    vec2 dir = direction;
    float len = length(dir);
    dir = len < 0.001 ? vec2(1.0, 0.0) : dir / len;
    vec2 perp = vec2(-dir.y, dir.x);
    float t = TIME * speed;

    // 沿风向拉长采样，风向上游密、下游被扯稀。
    vec2 stretched = vec2(dot(uv - vec2(0.5), perp) * 6.0, dot(uv - vec2(0.5), dir) * 2.2);
    float n = noise(stretched - vec2(0.0, t * 1.8));
    n += noise(stretched * 2.3 - vec2(0.0, t * 3.6)) * 0.5;
    n /= 1.5;

    float along = dot(uv - vec2(0.5), dir);          // -0.5 迎风 .. 0.5 顺风
    float across = abs(dot(uv - vec2(0.5), perp));

    // 雾团主体偏迎风侧，顺风侧拖出逐渐变稀的尾。
    float head = exp(-pow((along + 0.12) * 4.5, 2.0)) * exp(-across * 6.0);
    float tail = smoothstep(0.45, -0.25, along) * exp(-across * (3.0 + along * 4.0));
    float streak = smoothstep(0.45, 0.75, n);

    float a = (head * 1.3 + tail * (0.35 + streak) * 0.9);
    a *= smoothstep(0.55, 0.05, across + max(along, 0.0) * 0.25);
    // 顺风尾收到边内，避免角上切出直角。
    a *= smoothstep(0.5, 0.32, max(abs(uv.x - 0.5), abs(uv.y - 0.5)));
    a = clamp(a, 0.0, 1.0);

    if (a <= 0.02) discard;
    vec3 col = mix(color_fx.rgb * 0.65, min(color_fx.rgb * 1.5, vec3(1.0)), streak);
    COLOR = vec4(col, a * color_fx.a);
}
