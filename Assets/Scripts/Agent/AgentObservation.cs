using UnityEngine;

/// <summary>
/// Per-step observation captured by the embodied agent.
/// Contains everything needed for downstream training data export.
/// </summary>
public struct AgentObservation
{
    /// <summary>RGB image from the agent's eye camera.</summary>
    public Texture2D rgb;

    /// <summary>Grayscale depth image (linear, 0=near 1=far).</summary>
    public Texture2D depth;

    /// <summary>Agent world position.</summary>
    public Vector3 position;

    /// <summary>Agent world rotation (euler angles).</summary>
    public Vector3 rotation;

    /// <summary>Which floor the agent is currently on.</summary>
    public int currentFloor;

    /// <summary>Whether the last action succeeded (e.g. move not blocked by wall).</summary>
    public bool lastActionSuccess;

    /// <summary>Object currently held by the agent, null if empty-handed.</summary>
    public string heldObject;
}
