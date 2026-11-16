using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class TwoHandRollTests
{
    private static readonly FlightMotionSettings Direct = FlightMotionSettings.Default with
    {
        DragSmoothSeconds = 0,
        TurnSmoothSeconds = 0,
        InertiaCutoffEnabled = false,
        InertiaAccelerationBoostEnabled = false,
        InertiaDecelerationPerSecond = 0,
        DragAccelerationMultiplier = 1,
        TurnAccelerationMultiplier = 1,
        ZAccelerationMultiplier = 1,
        BrakeRampSeconds = 0
    };

    private static InputFrame Frame() => new(
        new(Quaternion.Identity, new(0, 1.7f, 0)), true,
        new(new(Quaternion.Identity, new(-0.3f, 1.2f, 0)), 0, 0, 0, true),
        new(new(Quaternion.Identity, new(0.3f, 1.2f, 0)), 0, 0, 0, true));

    private static InputFrame Held(InputFrame frame) => frame with
    {
        Left = frame.Left with { Turn = 1 },
        Right = frame.Right with { Turn = 1 }
    };

    private static InputFrame Rotated(InputFrame frame, Vector3 axis, float angle)
    {
        var rotation = Quaternion.CreateFromAxisAngle(axis, angle);
        return frame with
        {
            Left = frame.Left with { Pose = frame.Left.Pose with { Orientation = rotation } },
            Right = frame.Right with { Pose = frame.Right.Pose with { Orientation = rotation } }
        };
    }

    private static FreeFlightManipulator Begin(InputFrame frame)
    {
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(frame, 0.1f, Direct);
        engine.Update(Held(frame), 0.1f, Direct);
        return engine;
    }

    [Fact]
    public void StationaryRollUsesTiltedHeadUpAndReversesToGrabOrientation()
    {
        var axis = Vector3.Normalize(new Vector3(0, 1, 1));
        var frame = Frame() with
        {
            Head = Frame().Head with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 4) }
        };
        var engine = Begin(frame);
        var after = engine.Update(Rotated(Held(frame), axis, 0.6f), 0.1f, Direct);
        Near(Quaternion.CreateFromAxisAngle(axis, -0.6f), after.Orientation);
        var returned = engine.Update(Held(frame), 0.1f, Direct);
        Near(Quaternion.Identity, returned.Orientation);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(0, 1, -2)]
    public void StationaryRollPrefersPrincipalTrackerAxisOverHeadUp(float x, float y, float z)
    {
        var axis = Vector3.Normalize(new Vector3(x, y, z));
        var frame = Frame();
        frame = frame with
        {
            Trackers =
            [
                new("waist", frame.Head.Position - axis * 0.7f, true),
                new("foot", frame.Head.Position - axis * 1.6f, true),
                new("lost", new(100, 0, 0), false),
                new("invalid", new(float.NaN, 0, 0), true)
            ]
        };
        var engine = Begin(frame);
        var after = engine.Update(Rotated(Held(frame), axis, 0.5f), 0.1f, Direct);
        Near(Quaternion.CreateFromAxisAngle(axis, -0.5f), after.Orientation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousOrCoincidentTrackersFallBackToHeadUp(bool coincident)
    {
        var frame = Frame();
        Vector3 h = frame.Head.Position;
        frame = frame with
        {
            Trackers = coincident
                ? [new("same", h, true)]
                : [new("a", h + Vector3.UnitY, true), new("b", h - Vector3.UnitY, true),
                   new("c", h + Vector3.UnitZ, true), new("d", h - Vector3.UnitZ, true)]
        };
        var engine = Begin(frame);
        var after = engine.Update(Rotated(Held(frame), Vector3.UnitY, 0.5f), 0.1f, Direct);
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.5f), after.Orientation);
    }

    [Fact]
    public void TrackerAxisChangesWithNoControllerRotationDoNotMoveSpace()
    {
        var frame = Frame();
        var engine = Begin(frame);
        frame = Rotated(Held(frame), Vector3.UnitY, 0.4f);
        engine.Update(frame, 0.1f, Direct);
        foreach (Vector3 axis in new[] { Vector3.UnitZ, Vector3.UnitY, Vector3.UnitZ })
        {
            frame = frame with { Trackers = [new("foot", frame.Head.Position - axis, true)] };
            var before = engine.Offset;
            Assert.True(before.NearlyEquals(engine.Update(frame, 0.1f, Direct)));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.05f, 1)]
    [InlineData(0.05f, 0)]
    [InlineData(0.05f, 0.5f)]
    public void OffPoleRollKeepsObliqueTravelDirectionDuringHoldAndReleasedInertia(float smooth, float vectorMultiplier)
    {
        var settings = Direct with { TurnSmoothSeconds = smooth, VectorRotationMultiplier = vectorMultiplier };
        var initial = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 0.3f, -0.2f), new(2, 3, 4));
        var physicalTravel = Vector3.Normalize(new Vector3(1, 1, 0));
        Vector3 travel = Vector3.Transform(physicalTravel, initial.Orientation);
        var frame = Frame();
        var engine = new FreeFlightManipulator(initial);
        engine.Update(frame, 0.1f, settings);
        var dragging = frame with { Left = frame.Left with { Drag = 1 } };
        engine.Update(dragging, 0.1f, settings);
        engine.Update(dragging with
        {
            Left = dragging.Left with { Pose = dragging.Left.Pose with { Position = dragging.Left.Pose.Position - physicalTravel * 0.1f } }
        }, 0.1f, settings);
        engine.Update(frame, 0.1f, settings);
        Assert.True(engine.HasLinearInertia);
        engine.Update(Held(frame), 0.1f, settings);
        Vector3 pivot = (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f;

        // Input around physical Y has no X-pole component. Its output rolls
        // around the oblique travel axis, including under smoothing.
        for (int step = 1; step <= 5; step++)
        {
            Vector3 before = engine.Offset.Transform(pivot);
            var after = engine.Update(Rotated(Held(frame), Vector3.UnitY, step * 0.1f), 0.1f, settings);
            Near(travel * 0.1f, after.Transform(pivot) - before);
        }
        Assert.False(engine.Offset.NearlyEquals(initial));
        var released = Rotated(frame, Vector3.UnitY, 0.5f);
        engine.Update(released, 0.1f, settings);
        Assert.True(engine.HasAngularInertia);
        for (int step = 0; step < 5; step++)
        {
            Vector3 before = engine.Offset.Transform(pivot);
            var after = engine.Update(released, 0.1f, settings);
            Near(travel * 0.1f, after.Transform(pivot) - before);
        }
    }

    [Fact]
    public void ObliquePoleAndOffPoleComponentsBothContributeAndReverseTogether()
    {
        var pole = Vector3.Normalize(new Vector3(1, 1, 0));
        var frame = Frame();
        Vector3 midpoint = (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f;
        frame = frame with
        {
            Left = frame.Left with { Pose = frame.Left.Pose with { Position = midpoint - pole * 0.3f } },
            Right = frame.Right with { Pose = frame.Right.Pose with { Position = midpoint + pole * 0.3f } }
        };
        var engine = Begin(frame);
        Vector3 inputAxis = Vector3.UnitY;
        float angle = 0.4f;
        Vector3 offPole = Vector3.Normalize(Vector3.UnitY - pole * Vector3.Dot(pole, Vector3.UnitY));
        Vector3 expectedRotation = -pole * Vector3.Dot(inputAxis, pole) * angle
            - Vector3.UnitY * Vector3.Dot(inputAxis, offPole) * angle;
        var after = engine.Update(Rotated(Held(frame), inputAxis, angle), 0.1f, Direct);
        Near(Quaternion.CreateFromAxisAngle(Vector3.Normalize(expectedRotation), expectedRotation.Length()), after.Orientation);
        Near(midpoint, after.Transform(midpoint));
        Near(Quaternion.Identity, engine.Update(Held(frame), 0.1f, Direct).Orientation);
    }

    [Fact]
    public void CurrentBodyAxisIsUsedForNewInputAndReverseInput()
    {
        var frame = Frame();
        var engine = Begin(frame);
        engine.Update(Rotated(Held(frame), Vector3.UnitY, 0.3f), 0.1f, Direct);
        var tilted = Rotated(Held(frame), Vector3.UnitY, 0.3f) with
        {
            Trackers = [new("foot", frame.Head.Position - Vector3.UnitZ, true)]
        };
        Quaternion before = engine.Update(tilted, 0.1f, Direct).Orientation;
        Quaternion next = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f) * tilted.Left.Pose.Orientation);
        var moved = tilted with
        {
            Left = tilted.Left with { Pose = tilted.Left.Pose with { Orientation = next } },
            Right = tilted.Right with { Pose = tilted.Right.Pose with { Orientation = next } }
        };
        var after = engine.Update(moved, 0.1f, Direct);
        Near(Quaternion.Normalize(before * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.2f)), after.Orientation);
        Near(before, engine.Update(tilted, 0.1f, Direct).Orientation);
    }

    [Fact]
    public void PoleTurnStillSteersLinearInertia()
    {
        var frame = Frame();
        var engine = new FreeFlightManipulator(RigidPose.Identity);
        engine.Update(frame, 0.1f, Direct);
        var drag = frame with { Left = frame.Left with { Drag = 1 } };
        engine.Update(drag, 0.1f, Direct);
        engine.Update(drag with
        {
            Left = drag.Left with { Pose = drag.Left.Pose with { Position = drag.Left.Pose.Position - Vector3.UnitZ * 0.1f } }
        }, 0.1f, Direct);
        engine.Update(frame, 0.1f, Direct);
        engine.Update(Held(frame), 0.1f, Direct);
        var rotated = Rotated(Held(frame), Vector3.UnitX, 0.4f);
        engine.Update(rotated, 0.1f, Direct);
        Vector3 pivot = (frame.Left.Pose.Position + frame.Right.Pose.Position) * 0.5f;
        Vector3 before = engine.Offset.Transform(pivot);
        var after = engine.Update(rotated, 0.1f, Direct);
        Near(Vector3.Transform(Vector3.UnitZ * 0.1f, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.4f)),
            after.Transform(pivot) - before);
    }

    [Theory]
    [InlineData(0.02f, false)]
    [InlineData(0.2f, true)]
    public void BodyRollReleaseRespectsAngularCutoff(float angle, bool expectInertia)
    {
        var settings = Direct with { InertiaCutoffEnabled = true };
        var frame = Frame();
        var engine = Begin(frame);
        engine.Update(Rotated(Held(frame), Vector3.UnitY, angle), 0.1f, settings);
        engine.Update(Rotated(frame, Vector3.UnitY, angle), 0.1f, settings);
        Assert.Equal(expectInertia, engine.HasAngularInertia);
        if (expectInertia)
        {
            var before = engine.Offset.Orientation;
            var after = engine.Update(Rotated(frame, Vector3.UnitY, angle), 0.1f, settings);
            Near(Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, -angle) * before), after.Orientation);
        }
    }

    [Fact]
    public void BodyAxisParallelToPoleUsesOnlyPoleRotation()
    {
        var frame = Frame() with
        {
            Head = Frame().Head with { Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2) }
        };
        var engine = Begin(frame);
        Near(Quaternion.Identity,
            engine.Update(Rotated(Held(frame), Vector3.UnitY, 0.4f), 0.1f, Direct).Orientation);
        // Returning the ignored gesture does not generate roll either.
        engine.Update(Held(frame), 0.1f, Direct);
        Near(Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.4f),
            engine.Update(Rotated(Held(frame), Vector3.UnitX, 0.4f), 0.1f, Direct).Orientation);
    }

    private static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f, $"{expected} != {actual}");

    private static void Near(Quaternion expected, Quaternion actual) =>
        Assert.True(1 - MathF.Abs(Quaternion.Dot(expected, actual)) < 0.00001f, $"{expected} != {actual}");
}
