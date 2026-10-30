using System;

namespace SpectrumControl
{
    // A deterministic demonstration source, deliberately separate from desktop audio.
    public sealed class SpectrumPreview
    {
        private readonly double[] levels = new double[101];
        public double[] Step(double seconds, double deltaSeconds, double sensitivity, double decayMilliseconds, bool silence)
        {
            double gain = 0.15 + Math.Max(0, Math.Min(100, sensitivity)) / 70.0;
            double release = decayMilliseconds <= 0 ? 0 : Math.Exp(-Math.Max(0, deltaSeconds) * 1000 / decayMilliseconds);
            for (int i = 0; i < levels.Length; i++)
            {
                double t = i / (double)(levels.Length - 1);
                double envelope = Math.Pow(Math.Sin(Math.PI * t), 1.4);
                double target = silence ? 0 : Math.Min(1, envelope * gain *
                    Math.Abs(0.65 * Math.Sin(t * 30 + seconds * 1.5) + 0.22 * Math.Sin(t * 69 - seconds * 2.25)));
                levels[i] = target >= levels[i] ? target : target + (levels[i] - target) * release;
            }
            return levels;
        }
    }
}
