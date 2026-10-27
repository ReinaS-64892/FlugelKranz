namespace FlugelKranz.Core;

/// <summary>Converts persisted/UI identifiers at the boundary; movement execution uses mode types.</summary>
public static class MovementModes
{
    public static ManipulationMode Create(FlightMode selection, RigidPose offset) => selection switch
    {
        FlightMode.FreeFlight => new FreeFlightManipulator(offset),
        FlightMode.InfiniteWalking => new InfiniteWalkingManipulator(offset),
        _ => throw new ArgumentOutOfRangeException(nameof(selection))
    };

    public static FlightMode Selection(ManipulationMode mode) => mode switch
    {
        FreeFlightManipulator => FlightMode.FreeFlight,
        InfiniteWalkingManipulator => FlightMode.InfiniteWalking,
        _ => throw new ArgumentException("Mode has no persisted/UI identifier.", nameof(mode))
    };

    public static bool Matches(ManipulationMode mode, FlightMode selection) => Selection(mode) == selection;
}
