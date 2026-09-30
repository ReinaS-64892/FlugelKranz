namespace FlugelKranz.Core;

/// <summary>The hands participating in a logical Drag or Turn operation.</summary>
[Flags]
public enum ManipulationHands
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right
}
