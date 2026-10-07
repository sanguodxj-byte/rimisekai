shader_type canvas_item;

// 战斗结界与圣光 Shader：晶体六边形能量盾或升腾神圣光柱。
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 color_fx : source_color = vec4(0.2, 0.65, 1.0, 1.0);
uniform int mode : hint_range(0, 1) = 0; // 0 = 护盾能量晶壁, 1 = 治疗神圣光柱

void fragment() {
    vec2 uv = UV - vec2(0.5);
    float dist = length(uv);

    if (mode == 0) {
        // 晶体六边形能量护盾：扩散并共振
        float r = 0.38 + sin(progress * 3.14159) * 0.05;
        float rim = exp(-abs(dist - r) * 26.0);
        float fill = exp(-dist * 3.8) * 0.3;

        // 能量蜂窝晶格
        vec2 guv = uv * 16.0;
        float grid = abs(sin(guv.x) * sin(guv.y * 1.732));
        grid = smoothstep(0.82, 0.96, grid) * 0.45;

        float a = (rim + fill + grid) * sin(progress * 3.14159);
        if (a <= 0.01) discard;
        COLOR = vec4(color_fx.rgb, a * color_fx.a);
    } else {
        // 治疗升腾神圣光柱与漂浮光尘
        float beam_x = exp(-abs(uv.x) * 11.0);
        float beam_y = smoothstep(0.45, -0.45, uv.y - progress * 0.45);
        float particle = step(0.95, fract(sin(dot(uv + vec2(0.0, -progress * 1.5), vec2(27.1, 83.3))) * 43758.54));

        float a = (beam_x * beam_y * 0.85 + particle * 0.7) * sin(progress * 3.14159);
        if (a <= 0.01) discard;
        COLOR = vec4(color_fx.rgb, a * color_fx.a);
    }
}
