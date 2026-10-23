// Plan A: 色相旋转 shader（卡其 → 蓝灰）。
// 挂在 SpriteActor 的 Sprite2D 上，用 uniform hue_offset 控制偏移量。
// Godot 4 CanvasItem shader 自动处理 alpha，透明背景保持透明（不需要 ChromaKey）。
shader_type canvas_item;

// hue_offset: 0.0 = 原色，0.5 ≈ 180° 完全相反，0.55 ≈ 东北军灰蓝
uniform float hue_offset = 0.0;
uniform float saturation = 1.0;

vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4.gb, p.l * K.xy);
    vec4 q = mix(vec4(p.xw, p.y), vec4.wyx, step(c.b, c.g));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

void fragment() {
    vec4 tex = texture(TEXTURE, UV);
    // 保持 alpha 透明
    if (tex.a < 0.02) {
        COLOR = tex;
        return;
    }
    vec3 hsv = rgb2hsv(tex.rgb);
    hsv.x = fract(hsv.x + hue_offset);   // 旋转色相
    hsv.y *= saturation;                  // 饱和度微调
    vec3 rgb = hsv2rgb(hsv);
    COLOR = vec4(rgb, tex.a);
}
