namespace BdvEngine;

/// <summary>
/// Separable 9-tap Gaussian blur. Draws a fullscreen triangle and
/// samples the input texture along <c>u_direction</c> — set to
/// <c>(1/w, 0)</c> for horizontal and <c>(0, 1/h)</c> for vertical.
/// Two passes ping-pong = one full 2D Gaussian.
///
/// Kernel weights are a discrete Gaussian centred at 0, σ ≈ 2 — the
/// standard "wide-enough-to-halo, narrow-enough-to-stay-cheap" curve
/// for a bloom pipeline. Change the numbers to sharpen or widen the
/// halo; the shader will still evaluate 9 samples per pass.
/// </summary>
public sealed class BlurShader : Shader
{
    public BlurShader() : base("bloom_blur")
    {
        Load(VertexSource, FragmentSource);
    }

    // A "trick" fullscreen triangle: three vertices in normalised-
    // device coords, gl_VertexID picks which. No VBO needed; the
    // caller just draws 3 vertices from a bound empty VAO.
    private const string VertexSource = @"#version 410 core
out vec2 v_uv;
void main() {
    // (0,0) → (0,0), (2,0) → (2,0), (0,2) → (0,2)
    vec2 uv = vec2((gl_VertexID == 1) ? 2.0 : 0.0,
                   (gl_VertexID == 2) ? 2.0 : 0.0);
    v_uv = uv;
    gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
}";

    private const string FragmentSource = @"#version 410 core
in vec2 v_uv;

uniform sampler2D u_source;
uniform vec2      u_direction;   // (1/w, 0) or (0, 1/h)

out vec4 fragColor;

// 9-tap Gaussian — offsets 0..4 with mirrored weights.
const float w0 = 0.227027;
const float w1 = 0.194594;
const float w2 = 0.121622;
const float w3 = 0.054054;
const float w4 = 0.016216;

void main() {
    vec3 sum = texture(u_source, v_uv).rgb * w0;
    sum += texture(u_source, v_uv + u_direction * 1.0).rgb * w1;
    sum += texture(u_source, v_uv - u_direction * 1.0).rgb * w1;
    sum += texture(u_source, v_uv + u_direction * 2.0).rgb * w2;
    sum += texture(u_source, v_uv - u_direction * 2.0).rgb * w2;
    sum += texture(u_source, v_uv + u_direction * 3.0).rgb * w3;
    sum += texture(u_source, v_uv - u_direction * 3.0).rgb * w3;
    sum += texture(u_source, v_uv + u_direction * 4.0).rgb * w4;
    sum += texture(u_source, v_uv - u_direction * 4.0).rgb * w4;
    fragColor = vec4(sum, 1.0);
}";
}
