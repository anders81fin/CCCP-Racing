using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>A background only: the existing DayNight light and fog remain the world's lighting.
/// Small angular steps, two cloud noise samples and sparse stars suit the low resolution view.
/// No assets, global RNG, shader TIME, volumetrics or additional scene lights.</summary>
public sealed class RallySky
{
    readonly ShaderMaterial _material;
    double _seconds;
    float _driftUpdate;

    public RallySky(Godot.Environment env, bool winter)
    {
        _material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _material.SetShaderParameter("winter", winter ? 1f : 0f);
        env.Sky = new Sky
        {
            SkyMaterial = _material,
            RadianceSize = Sky.RadianceSizeEnum.Size32,
            ProcessMode = Sky.ProcessModeEnum.Incremental,
        };
        env.BackgroundMode = Godot.Environment.BGMode.Sky;
        // Previously the color background had no sky reflections. Keep the world's color ambient.
        env.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        // Distance fog still colors geometry; the shader itself blends into that exact horizon.
        env.FogSkyAffect = 0f;
    }

    public void SetLook(in DayNight.Look look, Vector3 lightDirection, float overcast)
    {
        _material.SetShaderParameter("horizon", look.Sky);
        _material.SetShaderParameter("light_color", look.SunColor);
        _material.SetShaderParameter("light_dir", lightDirection);
        _material.SetShaderParameter("light_energy", look.SunEnergy);
        // The existing light changes sides only at zero energy; its blue tint identifies the moon.
        _material.SetShaderParameter("moon", Math.Clamp((look.SunColor.B - look.SunColor.R) * 4f, 0f, 1f));
        float brightness = Math.Max(look.Sky.R, Math.Max(look.Sky.G, look.Sky.B));
        _material.SetShaderParameter("night", 1f - Mathf.SmoothStep(0.10f, 0.42f, brightness));
        _material.SetShaderParameter("overcast", Math.Clamp(overcast, 0f, 1f));
    }

    public void Tick(float dt)
    {
        _seconds += Math.Max(0f, dt);
        _driftUpdate += dt;
        if (_driftUpdate < 0.1f) return;
        _driftUpdate = 0;
        // Wrap only after many hours; motion belongs to the visual, never the race/replay clock.
        _material.SetShaderParameter("cloud_drift", (float)(_seconds % 100000.0) * 0.006f);
    }

    const string Code = """
shader_type sky;
render_mode disable_fog;

uniform vec4 horizon : source_color = vec4(0.62, 0.64, 0.62, 1.0);
uniform vec4 light_color : source_color = vec4(1.0);
uniform vec3 light_dir = vec3(0.4, 0.7, 0.5);
uniform float light_energy = 0.9;
uniform float night = 0.0;
uniform float moon = 0.0;
uniform float overcast = 0.0;
uniform float winter = 0.0;
uniform float cloud_drift = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}
float noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), f.x),
        mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0)), f.x), f.y);
}

void sky() {
    // Reflections are disabled. Avoid evaluating clouds and stars for any radiance update.
    if (AT_CUBEMAP_PASS) {
        COLOR = horizon.rgb;
    } else {
        vec3 ray = normalize(EYEDIR);
        vec3 d = normalize(floor(ray * 220.0 + 0.5) / 220.0);
        float height = max(d.y, 0.0);
        float gradient = floor(smoothstep(0.0, 0.85, height) * 24.0) / 24.0;
        vec3 blue = mix(vec3(0.42, 0.57, 0.65), vec3(0.39, 0.52, 0.65), winter);
        // Retain the authored warm sunset and pink dawn rather than replacing their palette.
        float warm = smoothstep(0.02, 0.25, horizon.r - horizon.b);
        vec3 zenith = mix(horizon.rgb * blue, horizon.rgb * vec3(0.58, 0.56, 0.79), warm);
        zenith = mix(zenith, horizon.rgb * 0.72, overcast * 0.8);
        vec3 color = mix(horizon.rgb, zenith, gradient);

        float facing = max(dot(d, light_dir), 0.0);
        float clear = 1.0 - overcast;
        float visible = smoothstep(0.0, 0.18, light_energy) * clear * clear;
        color += light_color.rgb * pow(facing, 18.0) * visible * (1.0 - moon) * 0.09;
        float disc = smoothstep(0.99968, 0.99982, facing);
        // A restrained crescent, with the same position as the world's moon light.
        vec3 offset_dir = normalize(light_dir + vec3(0.014, 0.004, 0.0));
        float shade = smoothstep(0.99968, 0.99982, dot(d, offset_dir));
        disc *= mix(1.0, 1.0 - shade * 0.85, moon);
        color = mix(color, mix(light_color.rgb, vec3(0.72, 0.79, 0.87), moon), disc * visible);

        vec2 stars = vec2(atan(d.z, d.x) * 120.0, asin(clamp(d.y, -1.0, 1.0)) * 120.0);
        vec2 star_cell = floor(stars);
        vec2 point = fract(stars);
        float star = step(0.991, hash(star_cell)) * step(0.37, point.x) * step(point.x, 0.66)
            * step(0.37, point.y) * step(point.y, 0.66);
        color += vec3(0.58, 0.66, 0.75) * star * night * clear * clear * smoothstep(0.03, 0.23, height);

        vec2 cloud_uv = d.xz / (height + 0.24) * 2.7 + vec2(cloud_drift, cloud_drift * 0.3);
        cloud_uv = floor(cloud_uv * 30.0) / 30.0;
        float field = noise(cloud_uv) * 0.75 + noise(cloud_uv * 2.1 + vec2(5.0)) * 0.25;
        float cover = smoothstep(mix(0.60, 0.27, overcast), mix(0.69, 0.48, overcast), field);
        cover *= smoothstep(0.035, 0.17, height);
        float tiers = floor(field * 6.0) / 6.0;
        vec3 cloud = mix(horizon.rgb * 0.88, horizon.rgb * 1.14 + vec3(0.035), tiers);
        cloud = mix(cloud, horizon.rgb * (0.69 + tiers * 0.25), overcast);
        cloud = mix(cloud, horizon.rgb * (0.42 + tiers * 0.35), night);
        color = mix(color, cloud, cover * mix(0.86, 0.98, overcast));
        COLOR = max(color, vec3(0.0));
    }
}
""";
}
