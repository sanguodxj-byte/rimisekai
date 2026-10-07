shader_type canvas_item;

// 第一人称极细纯直剑痕 Shader：纯净抗锯齿丝滑刀光，绝无噪点颗粒，纯正冰魄钴蓝（零绿感）。
// 严格在像素空间对齐斩击向量，确保剑痕倾角与切片多边形裁剪线 100% 绝对重合！
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform float angle : hint_range(-3.1416, 3.1416) = -0.62; // 真实屏幕弧度倾角
uniform vec4 color_core : source_color = vec4(1.0, 1.0, 1.0, 1.0);       // 极白锐利细核
uniform vec4 color_glow : source_color = vec4(0.22, 0.52, 1.0, 1.0);     // 纯正冰魄晶蓝（蓝绝对主导，微红平衡，严禁发绿）
uniform vec2 quad_size = vec2(1050.0, 680.0);                            // 视口像素尺寸
uniform float slash_width_px : hint_range(1.0, 30.0) = 10.0;             // 真实像素剑痕宽度

void fragment() {
    // 转换为真实屏幕像素偏移坐标，彻底消除非正方形视口导致的各向异性角度畸变！
    vec2 pos = (UV - vec2(0.5)) * quad_size;

    // 旋转对齐主斩击直线向量（在像素空间严格等比旋转）
    float c = cos(-angle);
    float s = sin(-angle);
    vec2 r_pos = vec2(pos.x * c - pos.y * s, pos.x * s + pos.y * c);

    // 沿直线平滑展开与伸长（像素单位）
    float expansion = sin(progress * 3.14159);
    float slash_len = (quad_size.x * 0.49) * expansion;
    float dist_along = abs(r_pos.x);

    if (dist_along > slash_len) {
        discard;
    }

    // 两端锋芒丝滑收尖
    float taper = 1.0 - smoothstep(slash_len * 0.72, slash_len + 0.001, dist_along);

    // -------------------------------------------------------------
    // 纯直丝滑细剑痕（严禁任何颗粒与噪点，高阶高斯平滑衰减）
    // -------------------------------------------------------------
    float main_dist = abs(r_pos.y);
    float max_cut_w = slash_width_px * 2.6 * taper + 2.0;
    if (main_dist > max_cut_w) {
        discard;
    }

    float norm_dist = main_dist / (slash_width_px * taper + 0.4);

    // 1. 激光级纯白细剑芯（极高频紧凑高斯衰减）
    float core = exp(-norm_dist * norm_dist * 55.0) * taper;

    // 2. 紧贴主刃的细长纯蓝光缘（纯净无颗粒渐变）
    float glow = exp(-norm_dist * 3.8) * taper;

    // 3. 伴随极微单轨副刀风（平滑连续线）
    float sub_blade = exp(-abs(r_pos.y - 5.0 * taper) * 0.20) * 0.35 * taper;

    // 色彩融合：纯白激光细芯 + 纯蓝无绿光弧
    vec4 col = mix(color_glow * (glow + sub_blade), color_core, clamp(core * 1.6, 0.0, 1.0));

    float alpha = clamp(core * 2.2 + glow * color_glow.a * 1.3 + sub_blade, 0.0, 1.0);
    alpha *= (1.0 - progress * progress * 0.85);

    if (alpha <= 0.01) {
        discard;
    }
    col.a = alpha;
    COLOR = col;
}
