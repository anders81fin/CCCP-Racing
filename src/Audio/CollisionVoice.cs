using System;
using CCCPRacing.Car;
using CCCPRacing.Track;

namespace CCCPRacing.Audio;

/// <summary>A car-body impact with a material response. One bass attack supports
/// irregular midrange crush/folds and smaller settling rattles; secondary folds
/// never restart the main impact. Synthesis uses its own deterministic event seed.</summary>
public sealed class CollisionVoice
{
    readonly float _rate;
    readonly float[] _phase = new float[6], _hz = new float[6], _decay = new float[6];
    uint _noise;
    float _age = 10, _length, _low, _mid, _brightLow, _pressureLow, _pressureSub, _fold, _nextFold, _bodyGain, _grainGain, _ringGain, _crackGain;
    ImpactMaterial _material;
    bool _broke;
    CollisionResponse _hit;
    public CollisionVoice(float sampleRate = 22050) => _rate = sampleRate;
    public bool Active => _hit.EnergyJoules > 0 && _age < _length;

    float Random()
    {
        _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
        return (_noise & 0xffffff) / 16777215f;
    }

    public bool Trigger(CollisionResponse hit, ImpactMaterial material = ImpactMaterial.Unknown, bool broke = false)
    {
        if (hit.EnergyJoules <= 0 || (_age < .085f && hit.EnergyJoules < _hit.EnergyJoules * 1.4f)) return false;
        _hit = hit; _material = material; _broke = broke;
        // Derive variation from recorded state: the same saved impact reproduces
        // the same detail after load/seek, without a new random-state stream.
        _noise = 0xCCCF1917u ^ BitConverter.SingleToUInt32Bits(hit.EnergyJoules) ^ ((uint)material << 20) ^ (broke ? 0x135791u : 0);
        _age = 0; _low = _mid = _brightLow = _pressureLow = _pressureSub = 0; _fold = 1; _nextFold = .012f + .025f * Random();
        float w = hit.Weight;
        _length = .22f + .95f * w;
        (_bodyGain, _grainGain, _ringGain, _crackGain) = material switch
        {
            ImpactMaterial.Metal => (.26f, .90f, .19f, .36f),
            ImpactMaterial.Wood => (.21f, .85f, .07f, .45f),
            ImpactMaterial.Stone => (.25f, 1.05f, .08f, .36f),
            ImpactMaterial.Snow => (.18f, .65f, .012f, .06f),
            ImpactMaterial.Ground => (.25f, .88f, .05f, .15f),
            ImpactMaterial.Soft => (.12f, .42f, .018f, .06f),
            _ => (.25f, .88f, .13f, .33f),
        };
        for (int k = 0; k < _phase.Length; k++)
        {
            float baseHz = material switch
            {
                ImpactMaterial.Wood => 220 + k * 245,
                ImpactMaterial.Stone => 180 + k * 310,
                ImpactMaterial.Snow or ImpactMaterial.Soft => 130 + k * 140,
                _ => 170 + k * 265,
            };
            _hz[k] = baseHz * (.83f + .34f * Random()) * (1.08f - .22f * w);
            _phase[k] = Random() * MathF.Tau;
            _decay[k] = (.009f + .022f * w) * (.7f + .6f * Random());
        }
        return true;
    }

    public void Clear() { _age = 10; _hit = default; _low = _mid = 0; }

    /// <summary>Restore the tail at its original age; no new attack on seek.</summary>
    public void Restore(CollisionResponse hit, ImpactMaterial material, bool broke, float age)
    {
        Clear();
        if (age < 0 || !Trigger(hit, material, broke)) return;
        for (int i = 0, n = (int)Math.Min(age * _rate, _length * _rate + 1); i < n; i++) Sample();
    }

    public float Sample()
    {
        if (!Active) return 0;
        float t = _age, w = _hit.Weight, dt = 1 / _rate;
        _age += dt;
        float noise = Random() * 2 - 1;
        _low += (noise - _low) * (1 - MathF.Exp(-MathF.Tau * 150 / _rate));
        float corner = _material is ImpactMaterial.Snow or ImpactMaterial.Soft ? 900 : 2000;
        _mid += (noise - _mid) * (1 - MathF.Exp(-MathF.Tau * corner / _rate));
        _brightLow += (noise - _brightLow) * (1 - MathF.Exp(-MathF.Tau * 3800 / _rate));
        float grain = _mid - _low, bright = _brightLow - _mid;

        if (t >= _nextFold && t < .1f + .35f * w)
        {
            _fold = .4f + .6f * Random();
            _nextFold = t + (.018f + .05f * Random()) * (1 + t * 3);
        }
        _fold *= MathF.Exp(-dt * 85);
        float crushDecay = _material == ImpactMaterial.Wood ? .045f + .08f * w : .05f + .19f * w;
        float crush = grain * (.15f + .95f * w) * _grainGain * (.38f + .95f * _fold)
            * MathF.Exp(-t / crushDecay);
        float crack = bright * _crackGain * (.12f + .55f * MathF.Sqrt(w)) * MathF.Exp(-t / (.004f + .012f * w));
        // Breaking wood/stone releases extra fine detail, not another bass boom.
        if (_broke) crush += grain * _grainGain * (.08f + .25f * w) * _fold * MathF.Exp(-t / (.08f + .12f * w));
        // A single broad pressure front, followed by a turbulent low body.
        // No sustained sine: it made the car sound like a struck hollow pipe.
        _pressureLow += (noise - _pressureLow) * (1 - MathF.Exp(-MathF.Tau * 260 / _rate));
        _pressureSub += (_pressureLow - _pressureSub) * (1 - MathF.Exp(-MathF.Tau * 75 / _rate));
        bool soft = _material is ImpactMaterial.Snow or ImpactMaterial.Soft;
        float pressureGain = (_bodyGain / .25f) * (soft ? .22f : 1f) * (.08f + 1.35f * w);
        float z = (t - .004f) / (.0035f + .005f * w);
        float front = (1 - 2 * z * z) * MathF.Exp(-z * z);
        float turbulence = (_pressureSub * 7 + (_pressureLow - _pressureSub) * 3.5f + grain * .55f)
            * MathF.Exp(-t / (.022f + .12f * w));
        float body = (front * .55f + turbulence) * pressureGain * (1 - MathF.Exp(-t * 2400));
        float blast = _brightLow * (soft ? .05f : .2f + .7f * w) * MathF.Exp(-t / (.003f + .009f * w));
        float metal = 0;
        for (int k = 0; k < _phase.Length; k++)
        {
            _phase[k] += MathF.Tau * _hz[k] / _rate;
            metal += MathF.Sin(_phase[k]) * MathF.Exp(-t / _decay[k]) / (1 + k * .35f);
        }
        metal *= _ringGain * .06f * (.2f + .8f * w) * (1 - MathF.Exp(-t * 2000));
        // A quieter irregular settling texture, delayed behind the main hit.
        float settle = grain * _grainGain * (.035f + .23f * w) * (.2f + _fold)
            * (1 - MathF.Exp(-t * 28)) * MathF.Exp(-t / (.08f + .24f * w));
        float end = Math.Clamp((_length - t) / .03f, 0, 1);
        return .94f * MathF.Tanh((blast + crack + body + crush + metal + settle) * 1.7f) * end;
    }
}
