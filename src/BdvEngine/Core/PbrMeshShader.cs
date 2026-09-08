using System.Numerics;

namespace BdvEngine;

/// <summary>PBR-lite shader — Cook-Torrance GGX with metallic/roughness, over up to
/// <see cref="MeshShader.MaxLights"/> scene lights (directional + point), ambient approximation and
/// Reinhard tone-map + gamma. Not full IBL, but gives imported glTF <c>pbrMetallicRoughness</c>
/// materials a physically-plausible look. The renderer routes <see cref="MaterialShading.Pbr"/> here.</summary>
public sealed class PbrMeshShader : MeshShader
{
    public PbrMeshShader() : base("mesh_pbr") => Load(Vert, Frag);

    public override void SetFrame(in FrameParams f)
    {
        SetUniform("u_proj", f.Proj);
        SetUniform("u_view", f.View);
        SetLights(f);
        SetShadow(f);
    }

    public override void SetObject(in Matrix4x4 model, in Matrix4x4 normalMatrix, Material material)
    {
        SetUniform("u_model", model);
        SetUniform("u_normalMatrix", normalMatrix);
        SetUniform("u_color", material.Color.ToVector4());
        SetUniform("u_metallic", material.Metallic);
        SetUniform("u_roughness", material.Roughness);
        if (material.DiffuseTexture != null)
        {
            material.DiffuseTexture.Activate(0);
            SetUniform("u_diffuse", 0);
        }
    }

    private const string Vert = @"#version 410 core
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec3 a_normal;
layout(location = 2) in vec2 a_uv;
uniform mat4 u_proj, u_view, u_model, u_normalMatrix;
out vec3 v_normal; out vec2 v_uv; out vec3 v_fragPos;
void main() {
    vec4 world = u_model * vec4(a_pos, 1.0);
    gl_Position = u_proj * u_view * world;
    v_fragPos = world.xyz;
    v_normal = (u_normalMatrix * vec4(a_normal, 0.0)).xyz;
    v_uv = a_uv;
}";

    /// <summary>Shared with the skinned variant so lighting can't drift between them.</summary>
    internal const string Frag = @"#version 410 core
#define MAX_LIGHTS 8
in vec3 v_normal; in vec2 v_uv; in vec3 v_fragPos;
uniform vec4 u_color;
uniform sampler2D u_diffuse;
uniform vec3 u_ambientColor, u_viewPos;
uniform float u_metallic, u_roughness;
uniform int u_lightCount;
uniform int u_lightType[MAX_LIGHTS];
uniform vec3 u_lightVec[MAX_LIGHTS];
uniform vec3 u_lightColor[MAX_LIGHTS];
uniform float u_lightRange[MAX_LIGHTS];
out vec4 fragColor;

const float PI = 3.14159265359;

uniform int u_shadowOn;
uniform mat4 u_lightViewProj;
uniform sampler2D u_shadowMap;
uniform float u_shadowBias, u_shadowTexel, u_shadowSoft, u_shadowStrength;

// Returns 1.0 in full light, (1 - strength) in full shadow. Only the sun casts.
float sunVisibility(vec3 fragPos, vec3 N, vec3 L) {
    if (u_shadowOn == 0) return 1.0;
    vec4 lp = u_lightViewProj * vec4(fragPos, 1.0);
    vec3 pr = lp.xyz / lp.w;
    pr = pr * 0.5 + 0.5;
    if (pr.z > 1.0) return 1.0;                 // beyond the light's far plane

    // Slope-scaled bias: surfaces edge-on to the sun span more depth per texel and need more
    // slack, while surfaces facing it need almost none (too much there causes peter-panning).
    float ndl = clamp(dot(N, L), 0.0, 1.0);
    float bias = max(u_shadowBias * (1.0 - ndl), u_shadowBias * 0.15);

    float lit = 0.0;
    for (int x = -1; x <= 1; ++x)
        for (int y = -1; y <= 1; ++y) {
            vec2 off = vec2(float(x), float(y)) * u_shadowTexel * u_shadowSoft;
            float d = texture(u_shadowMap, pr.xy + off).r;
            lit += (pr.z - bias) > d ? 0.0 : 1.0;
        }
    lit /= 9.0;
    return mix(1.0 - u_shadowStrength, 1.0, lit);
}


float distributionGGX(vec3 N, vec3 H, float rough) {
    float a = rough * rough;
    float a2 = a * a;
    float ndh = max(dot(N, H), 0.0);
    float d = ndh * ndh * (a2 - 1.0) + 1.0;
    return a2 / max(PI * d * d, 1e-6);
}
float geometrySchlick(float ndv, float rough) {
    float k = (rough + 1.0);
    k = (k * k) / 8.0;
    return ndv / (ndv * (1.0 - k) + k);
}
float geometrySmith(vec3 N, vec3 V, vec3 L, float rough) {
    return geometrySchlick(max(dot(N, V), 0.0), rough) *
           geometrySchlick(max(dot(N, L), 0.0), rough);
}
vec3 fresnelSchlick(float ct, vec3 F0) {
    return F0 + (1.0 - F0) * pow(clamp(1.0 - ct, 0.0, 1.0), 5.0);
}

void main() {
    vec4 tex = texture(u_diffuse, v_uv) * u_color;
    vec3 albedo = tex.rgb;

    vec3 N = normalize(v_normal);
    vec3 V = normalize(u_viewPos - v_fragPos);
    vec3 F0 = mix(vec3(0.04), albedo, u_metallic);

    vec3 Lo = vec3(0.0);
    for (int i = 0; i < u_lightCount; i++) {
        vec3 L; float att = 1.0;
        if (u_lightType[i] == 0) {
            L = normalize(u_lightVec[i]);
        } else {
            vec3 d = u_lightVec[i] - v_fragPos;
            float dist = length(d);
            L = d / max(dist, 1e-4);
            att = clamp(1.0 - dist / max(u_lightRange[i], 1e-3), 0.0, 1.0);
            att *= att;
        }
        vec3 H = normalize(V + L);
        vec3 radiance = u_lightColor[i] * att;

        float NDF = distributionGGX(N, H, u_roughness);
        float G   = geometrySmith(N, V, L, u_roughness);
        vec3  F   = fresnelSchlick(max(dot(H, V), 0.0), F0);

        vec3 spec = (NDF * G * F) / max(4.0 * max(dot(N, V), 0.0) * max(dot(N, L), 0.0), 1e-4);
        vec3 kd = (vec3(1.0) - F) * (1.0 - u_metallic);
        float ndl = max(dot(N, L), 0.0);
        float vis = (i == 0) ? sunVisibility(v_fragPos, N, L) : 1.0;
        Lo += (kd * albedo / PI + spec) * radiance * ndl * vis;
    }

    vec3 ambient = u_ambientColor * albedo;
    vec3 color = ambient + Lo;
    color = color / (color + vec3(1.0));       // Reinhard tone map
    color = pow(color, vec3(1.0 / 2.2));        // gamma
    fragColor = vec4(color, tex.a);
}";
}
