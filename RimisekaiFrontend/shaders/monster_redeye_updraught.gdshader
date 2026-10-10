shader_type canvas_item;

// 精英怪物的红眼气流。气从眼睛发出、向上拖走。
// 不描眼睛的形状，眼位由外部按每只怪物传入。
uniform vec4 color_fx : source_color = vec4(0.55, 0.0, 0.0, 1.0);
uniform float speed : hint_range(0.1, 3.0) = 1.0;
uniform vec2 eye_a = vec2(0.5, 0.5);
uniform vec2 eye_b = vec2(0.5, 0.5);
uniform int eye_count = 1;

float hash(float n) {
    return fract(sin(n * 127.1) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = fract(sin(dot(i, vec2(127.1, 311.7))) * 43758.5453);
    float b = fract(sin(dot(i + vec2(1.0, 0.0), vec2(127.1, 311.7))) * 43758.5453);
    float c = fract(sin(dot(i + vec2(0.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
    float d = fract(sin(dot(i + vec2(1.0, 1.0), vec2(127.1, 311.7))) * 43758.5453);
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// 一只眼的贡献：眼位上的红光，加上从该眼向上拖走的气。不描眼形。
float from_eye(vec2 uv, vec2 eye, float t) {
    vec2 d = uv - eye;
    float glow = exp(-(d.x * d.x * 4000.0 + d.y * d.y * 6000.0));

    float rise = eye.y - uv.y;
    float above = smoothstep(0.0, 0.015, rise);
    float sway = sin(rise * 10.0 - t * 3.0 + eye.x * 7.0) * rise * 0.12;
    float lane = abs(uv.x - eye.x - sway);
    float thread = exp(-lane * mix(46.0, 100.0, clamp(rise * 2.0, 0.0, 1.0)));
    float grain = noise(vec2(eye.x * 6.0, (uv.y - t * 0.4) * 9.0));
    float drag = thread * above * exp(-rise * 2.6) * smoothstep(0.28, 0.74, grain);

    return glow * 0.35 + drag * 1.1;
}

void fragment() {
    vec2 uv = UV;
    float t = TIME * speed;

    float a = from_eye(uv, eye_a, t);
    if (eye_count > 1) a += from_eye(uv, eye_b, t + 1.3);
    // 压高不透明度，否则半透明红盖在白线稿上会混成浅粉。
    a = clamp(a * 2.2, 0.0, 1.0);

    if (a <= 0.02) discard;
    vec3 col = mix(color_fx.rgb * 0.35, color_fx.rgb, smoothstep(0.15, 0.75, a));
    COLOR = vec4(col, a * color_fx.a);
}
