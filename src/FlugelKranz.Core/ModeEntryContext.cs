namespace FlugelKranz.Core;

/// <summary>The applied space state and physical input captured when selecting a mode.</summary>
public readonly record struct ModeEntryContext(
    RigidPose CurrentOffset,
    RigidPose OriginalOffset,
    InputFrame Frame,
    FlugelKranzSettings Settings);
