using System;
using DeepFocusForWindows.Services;
using FluentAssertions;
using Xunit;

namespace DeepFocusForWindows.Tests.Services;

/// <summary>
/// Tests for the ESC double-press detection logic extracted into a testable helper.
/// We test the timing logic without installing a real Win32 hook.
/// </summary>
public class EscDoublePressDetectorTests
{
    [Fact]
    public void TwoPressesWithinThreshold_RaisesEvent()
    {
        var detector = new EscDoublePressDetector(thresholdMs: 500);
        bool raised  = false;
        detector.DoublePressed += (_, _) => raised = true;

        detector.RecordPress(time: 1000u);
        detector.RecordPress(time: 1300u); // 300 ms later — within threshold

        raised.Should().BeTrue();
    }

    [Fact]
    public void TwoPressesOutsideThreshold_DoesNotRaiseEvent()
    {
        var detector = new EscDoublePressDetector(thresholdMs: 500);
        bool raised  = false;
        detector.DoublePressed += (_, _) => raised = true;

        detector.RecordPress(time: 1000u);
        detector.RecordPress(time: 2000u); // 1000 ms later — outside threshold

        raised.Should().BeFalse();
    }

    [Fact]
    public void ThirdPress_AfterDoubleDetected_StartsFresh()
    {
        var detector = new EscDoublePressDetector(thresholdMs: 500);
        int count = 0;
        detector.DoublePressed += (_, _) => count++;

        detector.RecordPress(1000u);
        detector.RecordPress(1200u); // double — fires
        detector.RecordPress(1300u); // starts fresh (no previous saved)
        detector.RecordPress(1350u); // second press of new pair — fires again

        count.Should().Be(2);
    }

    [Fact]
    public void SinglePress_DoesNotRaiseEvent()
    {
        var detector = new EscDoublePressDetector(thresholdMs: 500);
        bool raised  = false;
        detector.DoublePressed += (_, _) => raised = true;

        detector.RecordPress(1000u);

        raised.Should().BeFalse();
    }
}
