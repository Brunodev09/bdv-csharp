namespace BdvEngine;

/// <summary>
/// Additive composite of the blurred emissive target onto the main
/// framebuffer. Same fullscreen-triangle vertex as
/// <see cref="BlurShader"/>. Blend state is set by the caller
/// (<c>glBlendFunc(GL_ONE, GL_ONE)</c>); the shader just samples the
/// blur target and multiplies by <c>u_intensity</c> — the knob game
/// code turns to make things glow softer or blown-out.
/// </summary>
public sealed class BloomCompositeShader : Shader
{
    public BloomCompositeShader() : base("bloom_composite")
    {
        Load(VertexSource, FragmentSource);
    }

    private const string VertexSource = @"#version 410 core
out vec2 v_uv;
void main() {
    vec2 uv = vec2((gl_VertexID == 1) ? 2.0 : 0.0,
                   (gl_VertexID == 2) ? 2.0 : 0.0);
    v_uv = uv;
    gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_uv;

uniform sampler2D u_bloom;
uniform float     u_intensity;

out vec4 fragColor;

void main() {
    vec3 c = texture(u_bloom, v_uv).rgb * u_intensity;
    fragColor = vec4(c, 1.0);
}";
}
