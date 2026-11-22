using System;

using System.Globalization;

namespace GoghSpotifyMod;

internal static class PlaybackTimeline
{
    internal static double Position(double position, double duration, double sampleAge, bool playing, double rate)
    {
        if (!Finite(position) || !Finite(duration) || duration <= 0)
        {
            return 0;
        }

        double elapsed = 0;

        if (playing && Finite(sampleAge) && sampleAge > 0 && Finite(rate) && rate > 0)
        {
            elapsed = sampleAge * rate;
        }

        return Math.Clamp(position + elapsed, 0, duration);
    }

    internal static string Format(double seconds)
    {
        if (!Finite(seconds) || seconds < 0)
        {
            return "--:--";
        }

        // Bound formatting before converting to an integer, including malformed remote timeline data.
        long totalSeconds = (long)Math.Floor(Math.Min(seconds, 35999999));

        if (totalSeconds >= 3600)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1:00}:{2:00}",
                totalSeconds / 3600,
                totalSeconds / 60 % 60,
                totalSeconds % 60
            );
        }

        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", totalSeconds / 60, totalSeconds % 60);
    }

    internal static bool Finite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
