using System;

using GoghSpotifyMod;

static void AssertPosition(string scenario, double actual, double expected)
{
    if (Math.Abs(actual - expected) > 0.0001)
    {
        throw new Exception($"{scenario}: expected {expected}, got {actual}");
    }

    Console.WriteLine($"PASS {scenario}");
}

static void AssertTime(string scenario, double seconds, string expected)
{
    string actual = PlaybackTimeline.Format(seconds);

    if (actual != expected)
    {
        throw new Exception($"{scenario}: expected {expected}, got {actual}");
    }

    Console.WriteLine($"PASS {scenario}");
}

// Playing advances from the latest sample, including a nonstandard playback rate.
AssertPosition("Playing advances by elapsed sample age", PlaybackTimeline.Position(30, 205, 2.5, true, 1), 32.5);

AssertPosition("Playback rate scales elapsed sample age", PlaybackTimeline.Position(30, 205, 2.5, true, 1.5), 33.75);

// Pausing freezes the position even when the sample becomes older.
AssertPosition("Paused playback stays fixed", PlaybackTimeline.Position(30, 205, 20, false, 1), 30);

// Clamp around track limits rather than allowing the bar to escape its frame.
AssertPosition("Track end stops at total duration", PlaybackTimeline.Position(204, 205, 5, true, 1), 205);

AssertPosition("Negative position starts at zero", PlaybackTimeline.Position(-5, 205, 0, false, 1), 0);

AssertPosition("Unknown duration produces zero progress", PlaybackTimeline.Position(30, 0, 5, true, 1), 0);

// Invalid external values must not feed NaN or infinity to the Unity layout.
AssertPosition("Invalid position is rejected", PlaybackTimeline.Position(double.NaN, 205, 0, false, 1), 0);

AssertPosition("Invalid duration is rejected", PlaybackTimeline.Position(30, double.PositiveInfinity, 0, false, 1), 0);

// A new track uses its own sample and never inherits elapsed time from the old track.
AssertPosition("New track starts from its own sample", PlaybackTimeline.Position(0, 180, 0.25, true, 1), 0.25);

// Format both normal songs and longer media without rounding into the next second.
AssertTime("Zero time", 0, "0:00");

AssertTime("Minutes and seconds", 205.9, "3:25");

AssertTime("Hours minutes and seconds", 3661, "1:01:01");

AssertTime("Missing time", double.NaN, "--:--");
