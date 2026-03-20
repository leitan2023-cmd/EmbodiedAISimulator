/// <summary>
/// Discrete action space for the embodied agent.
/// Mirrors AI2-THOR / Habitat conventions for compatibility with standard benchmarks.
/// </summary>
public enum AgentAction
{
    MoveAhead,      // +gridSize forward (default 0.25m)
    MoveBack,       // -gridSize backward
    MoveLeft,       // strafe left
    MoveRight,      // strafe right
    RotateLeft,     // yaw -rotateStep degrees (default 30)
    RotateRight,    // yaw +rotateStep degrees
    LookUp,         // pitch -pitchStep degrees (clamped)
    LookDown,       // pitch +pitchStep degrees (clamped)
    PickupObject,   // grab nearest interactable within reach
    PutObject,      // release held object
    OpenObject,     // toggle open/close on nearest door or container
    Done            // declare task finished
}
