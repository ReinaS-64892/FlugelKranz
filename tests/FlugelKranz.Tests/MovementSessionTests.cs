using System.Numerics;
using FlugelKranz.Core;
using Xunit;

namespace FlugelKranz.Tests;

public class MovementSessionTests
{
    private static readonly FlugelKranzSettings Settings = FlugelKranzSettings.Default with
    {
        FreeFlight = FlightMotionSettings.Default with
        {
            DragSmoothSeconds = 0,
            TurnSmoothSeconds = 0,
            InertiaCutoffEnabled = false,
            InertiaDecelerationPerSecond = 0,
            ZAccelerationMultiplier = 1
        }
    };

    [Fact]
    public void EnteringFreeFlightPreservesTheAppliedPoseAndClearsThePreviousGrip()
    {
        var walking = new InfiniteWalkingManipulator(RigidPose.Identity);
        var session = new MovementSession(walking, RigidPose.Identity, RigidPose.Identity);
        session.Update(Frame(0, 0), 0.01f, Settings);
        session.Update(Frame(1, 0), 0.01f, Settings);
        var applied = session.Update(Frame(1, 1), 0.01f, Settings);
        Assert.True(walking.IsDragging);

        var flight = new FreeFlightManipulator(RigidPose.Identity);
        session.Select(flight, applied, Frame(), Settings);

        Assert.Same(flight, session.ActiveMode);
        Assert.Equal(applied, flight.Offset);
        Assert.False(walking.IsDragging);
        Assert.Equal(applied, session.Update(Frame(1, 2), 0.01f, Settings));
        Assert.False(flight.IsDragging);
    }

    [Fact]
    public void TransitionHandsItsFinalPoseToTheDestinationAndRequiresInputRearm()
    {
        var current = TiltedOffset();
        var session = CreateFlight(current);
        var walking = new InfiniteWalkingManipulator(RigidPose.Identity);
        session.Select(walking, current, Frame(), Settings);
        Assert.IsType<InfiniteWalkingTransition>(session.ActiveMode);
        Assert.False(session.ActiveMode.IsDragging);

        var halfway = session.Update(Frame(1, 0), 0.5f, Settings);
        Assert.InRange(halfway.Transform(DefaultPivot).Y,
            RigidPose.Identity.Transform(DefaultPivot).Y, current.Transform(DefaultPivot).Y);
        var applied = session.Update(Frame(1, 1), 0.5f, Settings);
        Assert.Same(walking, session.ActiveMode);
        Assert.Equal(InfiniteWalkingTransition.CreateTarget(current, RigidPose.Identity, DefaultPivot), applied);
        Assert.Equal(applied, walking.Offset);
        Assert.Equal(applied, session.Update(Frame(1, 2), 0.01f, Settings));
        Assert.False(walking.IsDragging);

        session.Update(Frame(0, 2), 0.01f, Settings);
        session.Update(Frame(1, 2), 0.01f, Settings);
        var moved = session.Update(Frame(1, 3), 0.01f, Settings);
        Assert.Equal(applied.Position.Y - 1, moved.Position.Y, 4);
        Assert.Equal(applied.Position.X, moved.Position.X);
        Assert.Equal(applied.Position.Z, moved.Position.Z);
    }

    [Fact]
    public void SwitchingBackDuringTransitionStartsFromTheAppliedIntermediatePose()
    {
        var current = TiltedOffset();
        var session = CreateFlight(current);
        session.Select(new InfiniteWalkingManipulator(current), current, Frame(), Settings);
        var intermediate = session.Update(Frame(), 0.3f, Settings);

        var flight = new FreeFlightManipulator(RigidPose.Identity);
        session.Select(flight, intermediate, Frame(), Settings);

        Assert.Same(flight, session.ActiveMode);
        Assert.Equal(intermediate, session.Update(Frame(), 0.1f, Settings));
        Assert.False(flight.HasLinearInertia);
        Assert.False(flight.HasAngularInertia);
    }

    [Fact]
    public void ReleaseRetainsTheAppliedPoseAndClearsFreeFlightInertia()
    {
        var session = CreateFlight(RigidPose.Identity);
        session.Update(Frame(0, 0), 0.01f, Settings);
        session.Update(Frame(1, 0), 0.01f, Settings);
        session.Update(Frame(1, 1), 0.01f, Settings);
        var applied = session.Update(Frame(0, 1), 0.01f, Settings);
        var flight = Assert.IsType<FreeFlightManipulator>(session.SelectedMode);
        Assert.True(flight.HasLinearInertia);

        session.Release(applied);

        Assert.False(flight.HasLinearInertia);
        Assert.Equal(applied, session.Update(Frame(), 0.1f, Settings));
    }

    [Fact]
    public void ReleaseDuringModeTransitionRestartsFromTheRetainedPose()
    {
        var current = TiltedOffset();
        var session = CreateFlight(current);
        session.Select(new InfiniteWalkingManipulator(current), current, Frame(), Settings);
        var intermediate = session.Update(Frame(), 0.3f, Settings);

        session.Release(intermediate);

        Assert.Equal(intermediate, session.ActiveMode.Offset);
        Assert.Equal(intermediate, session.Update(Frame(), 0, Settings));
        var completed = session.Update(Frame(), 1, Settings);
        Assert.Equal(InfiniteWalkingTransition.CreateTarget(intermediate, RigidPose.Identity, DefaultPivot), completed);
        Assert.IsType<InfiniteWalkingManipulator>(session.ActiveMode);
    }

    [Fact]
    public void FullResetCancelsModeTransitionAndKeepsTheSelectedMode()
    {
        var current = TiltedOffset();
        var session = CreateFlight(current);
        var walking = new InfiniteWalkingManipulator(current);
        session.Select(walking, current, Frame(), Settings);
        session.Update(Frame(), 0.3f, Settings);

        session.Reset(RigidPose.Identity);

        Assert.Same(walking, session.ActiveMode);
        Assert.False(session.IsTransitioning);
        Assert.Equal(RigidPose.Identity, session.Update(Frame(1, 1), 1, Settings));
    }

    [Fact]
    public void ReleaseDuringSpaceResetCancelsItWithoutJumpingToTheTarget()
    {
        var current = TiltedOffset();
        var walking = new InfiniteWalkingManipulator(current);
        var session = new MovementSession(walking, current, RigidPose.Identity);
        Assert.True(session.StartSpaceReset(Frame(), Settings));
        var intermediate = session.Update(Frame(), 0.25f, Settings);

        session.Release(intermediate);

        Assert.Same(walking, session.ActiveMode);
        Assert.Equal(intermediate, session.Update(Frame(), 1, Settings));
    }

    [Fact]
    public void SelectingAnotherModeDuringSpaceResetUsesTheAppliedPose()
    {
        var current = TiltedOffset();
        var session = CreateFlight(current);
        Assert.True(session.StartSpaceReset(Frame(), Settings));
        var intermediate = session.Update(Frame(), 0.25f, Settings);

        session.Select(new InfiniteWalkingManipulator(current), intermediate, Frame(), Settings);

        Assert.IsType<InfiniteWalkingTransition>(session.ActiveMode);
        Assert.Equal(intermediate, session.ActiveMode.Offset);
        Assert.Equal(InfiniteWalkingTransition.CreateTarget(intermediate, RigidPose.Identity, DefaultPivot),
            session.Update(Frame(), 1, Settings));
    }

    [Fact]
    public void HeightResetWorksWithoutHeadTrackingButHorizontalResetWaitsForIt()
    {
        var untracked = Frame() with { HeadTracked = false };
        var flight = CreateFlight(TiltedOffset());
        Assert.False(flight.StartSpaceReset(untracked, Settings));
        var walking = new MovementSession(new InfiniteWalkingManipulator(TiltedOffset()), TiltedOffset(), RigidPose.Identity);
        Assert.True(walking.StartSpaceReset(untracked, Settings));
        Assert.Equal(0, walking.Update(untracked, 1, Settings).Position.Y);
    }

    [Fact]
    public void AnotherModeCanDefineItsOwnEntryWithoutChangingSessionDispatch()
    {
        var session = CreateFlight(TiltedOffset());
        var destination = new AdditionalMode();
        session.Select(destination, TiltedOffset(), Frame(), Settings);
        var intermediate = session.Update(Frame(), 0.5f, Settings);
        Assert.Equal(4, intermediate.Position.Y, 4);

        var complete = session.Update(Frame(), 0.5f, Settings);

        Assert.Equal(6, complete.Position.Y);
        Assert.Same(destination, session.ActiveMode);
        Assert.Equal(complete, destination.Offset);
        Assert.Equal(complete, session.Update(Frame(), 0.1f, Settings));
    }

    [Theory]
    [InlineData(TurnOrigin.Head)]
    [InlineData(TurnOrigin.TrackedElementsMidpoint)]
    public void EntryUsesTheConfiguredSingleHandTurnPivot(TurnOrigin origin)
    {
        var current = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 2.9f, -0.3f), new(1, 5, 3));
        var frame = EntryFrame(ManipulationHands.None);
        var settings = Settings with { FreeFlight = Settings.FreeFlight with { TurnOrigin = origin } };
        Vector3 pivot = origin == TurnOrigin.Head ? frame.Head.Position
            : (frame.Head.Position + frame.Left.Pose.Position + frame.Right.Pose.Position) / 3;
        Vector3 startPoint = current.Transform(pivot);
        var session = CreateFlight(current);

        session.Select(new InfiniteWalkingManipulator(current), current, frame, settings);
        // Subsequent physical movement must not replace the pivot captured at entry.
        var laterFrame = frame with { Head = frame.Head with { Position = new(2, 3, 4) } };
        Vector3 halfwayPoint = session.Update(laterFrame, 0.5f, settings).Transform(pivot);
        Vector3 landingPoint = session.Update(laterFrame, 0.5f, settings).Transform(pivot);

        Assert.Equal(startPoint.X, halfwayPoint.X, 4);
        Assert.Equal(startPoint.Z, halfwayPoint.Z, 4);
        Assert.Equal((startPoint.Y + pivot.Y) / 2, halfwayPoint.Y, 4);
        Assert.Equal(startPoint.X, landingPoint.X, 4);
        Assert.Equal(startPoint.Z, landingPoint.Z, 4);
        Assert.Equal(pivot.Y, landingPoint.Y, 4);
    }

    [Theory]
    [InlineData(ManipulationHands.Left)]
    [InlineData(ManipulationHands.Right)]
    [InlineData(ManipulationHands.Both)]
    public void EntryCapturesTheDragPivotBeforeReleasingThePreviousMode(ManipulationHands hands)
    {
        var current = new RigidPose(Quaternion.CreateFromYawPitchRoll(0.4f, 2.9f, -0.3f), new(1, 5, 3));
        var session = CreateFlight(current);
        session.Update(EntryFrame(ManipulationHands.None), 0.01f, Settings);
        var frame = EntryFrame(hands);
        session.Update(frame, 0.01f, Settings);
        Vector3 pivot = hands switch
        {
            ManipulationHands.Left => frame.Left.Pose.Position,
            ManipulationHands.Right => frame.Right.Pose.Position,
            _ => (frame.Left.Pose.Position + frame.Right.Pose.Position) / 2
        };
        Vector3 startPoint = current.Transform(pivot);
        var flight = session.SelectedMode;
        Assert.True(flight.IsDragging);

        session.Select(new InfiniteWalkingManipulator(current), current, frame, Settings);
        Assert.False(flight.IsDragging);
        Vector3 landingPoint = session.Update(frame, 1, Settings).Transform(pivot);

        Assert.Equal(startPoint.X, landingPoint.X, 4);
        Assert.Equal(startPoint.Z, landingPoint.Z, 4);
        Assert.Equal(pivot.Y, landingPoint.Y, 4);
    }

    private static InputFrame EntryFrame(ManipulationHands dragHands) => new(
        new(Quaternion.Identity, new(0.4f, 1.7f, -0.2f)), true,
        new(new(Quaternion.Identity, new(-0.3f, 1.2f, 0.1f)), dragHands.HasFlag(ManipulationHands.Left) ? 1 : 0, 0, 0, true),
        new(new(Quaternion.Identity, new(0.6f, 1.1f, 0.4f)), dragHands.HasFlag(ManipulationHands.Right) ? 1 : 0, 0, 0, true));

    private static readonly Vector3 DefaultPivot = new(0, 1.7f / 3, 0);

    private static MovementSession CreateFlight(RigidPose current) =>
        new(new FreeFlightManipulator(current), current, RigidPose.Identity);

    private static RigidPose TiltedOffset() => new(Quaternion.CreateFromYawPitchRoll(0.4f, 0.7f, -0.3f), new(1, 2, 3));

    private static InputFrame Frame(float drag = 0, float handY = 0) => new(
        new(Quaternion.Identity, new(0, 1.7f, 0)), true,
        new(new(Quaternion.Identity, new(-0.3f, handY, 0)), drag, 0, 0, true),
        new(new(Quaternion.Identity, new(0.3f, handY, 0)), 0, 0, 0, true));

    private sealed class AdditionalMode : ManipulationMode
    {
        public override string ResetMessage => "Additional mode reset";
        public override ManipulationMode CreateAlternate(RigidPose current) => new FreeFlightManipulator(current);
        protected override void ReleaseMotion() { }
        public override RigidPose Update(InputFrame frame, float elapsedSeconds, FlugelKranzSettings settings) => Offset;
        public override MovementMode EnterFrom(ManipulationMode previous, ModeEntryContext context) =>
            new AdditionalTransition(context.CurrentOffset, context.CurrentOffset with { Position = new(1, 6, 3) }, this);
        public override MovementMode CreateReset(InputFrame frame, RigidPose original, FlugelKranzSettings settings) =>
            new AdditionalTransition(Offset, original, this);
    }

    private sealed class AdditionalTransition(RigidPose start, RigidPose target, ManipulationMode destination)
        : MovementTransition(start, target, destination);
}
