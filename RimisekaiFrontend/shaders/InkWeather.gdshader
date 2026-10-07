shader_type canvas_item;

// 天气特效（线稿单色）：在室外房间格内画雨丝、风线与落雪。
// 颜色只用墨色（InkStyle.Line 的银白），靠 alpha 控制浓淡，不引入新色相。
// 模式：0 小雨 1 暴雨 2 雷雨（带白闪） 3 大风 4 小雪 5 大雪 6 暴雪。

uniform int mode = 0;
uniform float time_sec = 0.0;

// 5x5 室外格掩码：1 = 该格是室外房间，特效只画在这里。
uniform float mask[25];

// 面板像素尺寸、网格原点与单格尺寸：UV → 像素 → 格坐标。
uniform vec2 panel_size = vec2(960.0, 540.0);
uniform vec2 grid_origin = vec2(30.0, 30.0);
uniform vec2 cell_size = vec2(180.0, 96.0);

uniform vec4 ink_color : source_color = vec4(0.914, 0.937, 0.918, 1.0);
uniform float strength = 0.5;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

// 斜向雨丝：一列一根短划，随时间下落。keep 为有雨的列比例，len 为划长。
float rain_layer(vec2 f, float t, float cols, float speed, float slant, float keep, float seed, float len) {
    vec2 p = f;
    p.x += p.y * slant;
    float cx = fract(p.x * cols);
    float col = floor(p.x * cols);
    float h = hash(vec2(col, seed));
    if (h > keep)
        return 0.0;
    float phase = fract(p.y - t * speed * (0.7 + h * 0.6) + h * 19.0);
    float head = smoothstep(len, 0.0, phase);
    float x = smoothstep(0.10, 0.02, abs(cx - 0.5));
    return x * head * (0.6 + 0.4 * h);
}

// 落雪：飘点带正弦横摆，speed 慢、点圆。
float snow_layer(vec2 f, float t, float cols, float speed, float drift, float keep, float seed, float size) {
    float cx = fract(f.x * cols);
    float col = floor(f.x * cols);
    float h = hash(vec2(col, seed));
    if (h > keep)
        return 0.0;
    float h2 = hash(vec2(col, seed + 37.0));
    float sway = sin(t * (0.6 + h2 * 0.8) + h * 6.2832) * drift;
    float x = cx - 0.5 + sway;
    float y = fract(f.y - t * speed * (0.4 + h * 0.5) + h * 11.0) - 0.1;
    float d = length(vec2(x * 2.2, y * 1.6));
    return smoothstep(size, size * 0.35, d);
}

// 风线：横扫的短划，比雨更平更稀。
float wind_layer(vec2 f, float t, float rows, float speed, float keep, float seed) {
    float ry = fract(f.y * rows);
    float row = floor(f.y * rows);
    float h = hash(vec2(row, seed));
    if (h > keep)
        return 0.0;
    float x = fract(f.x - t * speed * (0.8 + h * 0.5) + h * 17.0);
    float line = smoothstep(0.16, 0.02, x) * smoothstep(0.16, 0.03, abs(ry - 0.5));
    return line;
}

// 雷雨白闪：每 6 秒一个窗口，掷中的窗口先主闪后回闪，都按指数衰减。
float lightning(float t) {
    float window = floor(t / 6.0);
    float fh = hash(vec2(window, 7.0));
    if (fh < 0.45)
        return 0.0;
    float k = fract(t / 6.0);
    float main = exp(-k * 22.0);
    float echo = exp(-abs(k - 0.16) * 60.0) * 0.5;
    return clamp(main + echo, 0.0, 1.0);
}

void fragment() {
    float a = 0.0;
    vec2 px = UV * panel_size;
    vec2 gp = (px - grid_origin) / cell_size;
    vec2 ci = floor(gp);
    bool inside = ci.x >= 0.0 && ci.y >= 0.0 && ci.x < 5.0 && ci.y < 5.0;
    if (inside) {
        int cell = int(ci.y) * 5 + int(ci.x);
        if (mask[cell] >= 0.5) {
            vec2 f = fract(gp);
            float t = time_sec;

            if (mode == 0) {
                a = rain_layer(f, t, 9.0, 0.55, 0.10, 0.55, 3.0, 0.16)
                  + 0.6 * rain_layer(f, t, 13.0, 0.75, 0.12, 0.40, 8.0, 0.13);
            } else if (mode == 1) {
                a = rain_layer(f, t, 12.0, 0.85, 0.16, 0.85, 3.0, 0.20)
                  + rain_layer(f, t, 17.0, 1.10, 0.18, 0.70, 8.0, 0.17)
                  + 0.7 * rain_layer(f, t, 23.0, 1.35, 0.20, 0.55, 15.0, 0.14);
            } else if (mode == 2) {
                a = rain_layer(f, t, 13.0, 1.00, 0.22, 0.90, 3.0, 0.20)
                  + rain_layer(f, t, 19.0, 1.30, 0.24, 0.70, 8.0, 0.16)
                  + lightning(t) * 0.5;
            } else if (mode == 3) {
                a = wind_layer(f, t, 7.0, 0.9, 0.50, 5.0)
                  + 0.7 * wind_layer(f, t, 11.0, 1.4, 0.35, 12.0);
            } else if (mode == 4) {
                a = snow_layer(f, t, 7.0, 0.10, 0.10, 0.50, 21.0, 0.10)
                  + 0.7 * snow_layer(f, t, 11.0, 0.14, 0.14, 0.35, 33.0, 0.07);
            } else if (mode == 5) {
                a = snow_layer(f, t, 9.0, 0.16, 0.16, 0.75, 21.0, 0.12)
                  + snow_layer(f, t, 13.0, 0.22, 0.20, 0.55, 33.0, 0.08)
                  + 0.7 * snow_layer(f, t, 17.0, 0.28, 0.24, 0.40, 46.0, 0.06);
            } else if (mode == 6) {
                vec2 g = f;
                g.x += t * 0.25;
                a = snow_layer(g, t, 10.0, 0.28, 0.35, 0.90, 21.0, 0.11)
                  + snow_layer(g, t, 14.0, 0.36, 0.40, 0.70, 33.0, 0.08)
                  + wind_layer(f, t, 9.0, 1.6, 0.40, 12.0);
            }
        }
    }

    COLOR = vec4(ink_color.rgb, clamp(a, 0.0, 1.0) * strength);
}
