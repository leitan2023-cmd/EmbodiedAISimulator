using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Discrete-action embodied agent controller.
/// Provides a Step(action) API that external code (scripted policy, ML-Agents, socket bridge)
/// can call to drive the agent and receive observations.
///
/// Setup:
///   1. Attach to a GameObject with a CharacterController.
///   2. Create child GameObjects "RGBCamera" and "DepthCamera", each with a Camera component.
///   3. Assign the depth camera's replacement shader material (Hidden/EmbodiedAI/LinearDepth).
///   4. Or use AgentSetupHelper to auto-configure from the editor.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EmbodiedAgentController : MonoBehaviour
{
    [Header("Movement")]
    public float moveStep = 0.25f;
    public float rotateStep = 30f;
    public float maxPitch = 60f;

    [Header("Cameras")]
    public Camera rgbCamera;
    public Camera depthCamera;

    [Header("Capture")]
    public int imageWidth = 300;
    public int imageHeight = 300;
    public float depthFar = 10f;

    [Header("Interaction")]
    public float interactRange = 1.5f;

    [Header("State")]
    public int currentFloor = 1;

    private CharacterController cc;
    private float currentPitch;
    private bool lastActionSuccess;
    private GameObject heldObject;

    // Reusable render textures — created once, reused every step
    private RenderTexture rgbRT;
    private RenderTexture depthRT;
    private Texture2D rgbTex;
    private Texture2D depthTex;
    private Material depthMaterial;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        currentPitch = 0f;

        // Auto-find cameras if not assigned
        if (rgbCamera == null)
        {
            Transform rgbT = transform.Find("RGBCamera");
            if (rgbT != null) rgbCamera = rgbT.GetComponent<Camera>();
        }

        if (depthCamera == null)
        {
            Transform depthT = transform.Find("DepthCamera");
            if (depthT != null) depthCamera = depthT.GetComponent<Camera>();
        }

        // Disable auto-rendering on both cameras — we render manually in CaptureCamera()
        if (rgbCamera != null) rgbCamera.enabled = false;

        InitRenderTextures();
        ConfigureDepthCamera();
    }

    private void Start()
    {
        // Wait one frame for scene to finish building, then auto-find safe spawn
        StartCoroutine(AutoSpawnCoroutine());
    }

    private System.Collections.IEnumerator AutoSpawnCoroutine()
    {
        // Wait for MansionSceneBuilder to finish (it runs in Start too)
        yield return null;
        yield return null;

        // Diagnostic: count colliders
        int meshColliders = FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Length;
        int boxColliders = FindObjectsByType<BoxCollider>(FindObjectsSortMode.None).Length;
        Debug.Log($"[Agent] Scene colliders: MeshCollider={meshColliders}, BoxCollider={boxColliders}");

        // Try to find a safe spawn point on the floor
        if (TryFindSafeSpawn(out Vector3 safePos))
        {
            Teleport(safePos, 0f);
            Debug.Log($"[Agent] Auto-spawned at ({safePos.x:F1}, {safePos.y:F1}, {safePos.z:F1})");
        }
        else
        {
            Debug.LogWarning("[Agent] Could not find safe spawn point. Move the agent manually to inside a room.");
        }
    }

    /// <summary>
    /// Raycast downward from candidate positions to find a walkable floor surface.
    /// Scans a grid across the scene to find a point that has floor below and no wall overlap.
    /// </summary>
    private bool TryFindSafeSpawn(out Vector3 result)
    {
        result = Vector3.zero;

        // Scan a grid of candidate positions
        for (float x = 2f; x < 20f; x += 1f)
        {
            for (float z = 2f; z < 20f; z += 1f)
            {
                Vector3 testOrigin = new(x, 5f, z);

                // Raycast down to find floor
                if (!Physics.Raycast(testOrigin, Vector3.down, out RaycastHit hit, 10f))
                    continue;

                // Check we hit a floor (not a wall or furniture on upper floors)
                if (hit.point.y > 1f) continue; // too high, probably not ground floor

                // Check the landing spot is clear (no collider overlap at agent size)
                Vector3 candidate = hit.point + Vector3.up * 0.1f;
                if (Physics.CheckCapsule(
                    candidate + Vector3.up * cc.radius,
                    candidate + Vector3.up * (cc.height - cc.radius),
                    cc.radius * 0.9f))
                    continue; // something is blocking this spot

                result = candidate;
                return true;
            }
        }

        return false;
    }

    private void InitRenderTextures()
    {
        rgbRT = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
        depthRT = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32);
        rgbTex = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
        depthTex = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false);
    }

    private void ConfigureDepthCamera()
    {
        if (depthCamera == null) return;

        Shader depthShader = Shader.Find("Hidden/EmbodiedAI/LinearDepth");
        if (depthShader != null)
        {
            // NOTE: SetReplacementShader is a Built-in RP feature and does NOT work in URP.
            // In URP, this call is silently ignored — the depth camera will render normal RGB.
            // TODO: Replace with a ScriptableRendererFeature or CommandBuffer material override for proper URP depth capture.
            depthCamera.SetReplacementShader(depthShader, "RenderType");
            Debug.LogWarning("[Agent] SetReplacementShader does not work in URP. Depth capture will produce RGB instead of depth. Use a ScriptableRendererFeature for proper depth in URP.");
        }
        else
        {
            Debug.LogWarning("[Agent] LinearDepth shader not found. Depth capture will use default rendering.");
        }

        depthCamera.farClipPlane = depthFar;
        // Depth camera should not output to screen
        depthCamera.enabled = false;
    }

    private void OnDestroy()
    {
        if (rgbRT != null) rgbRT.Release();
        if (depthRT != null) depthRT.Release();
        if (rgbTex != null) Destroy(rgbTex);
        if (depthTex != null) Destroy(depthTex);
    }

    // =========================================================================
    //  Public API — this is what external code calls
    // =========================================================================

    /// <summary>
    /// Execute one discrete action and return the resulting observation.
    /// This is the primary interface for scripted policies, RL bridges, etc.
    /// </summary>
    public AgentObservation Step(AgentAction action)
    {
        lastActionSuccess = ExecuteAction(action);
        Physics.SyncTransforms();
        return CaptureObservation();
    }

    /// <summary>
    /// Capture the current observation without taking an action.
    /// Useful for the initial observation at episode start.
    /// </summary>
    public AgentObservation Observe()
    {
        return CaptureObservation();
    }

    /// <summary>
    /// Teleport the agent to a specific position and rotation.
    /// Used for episode reset / spawn.
    /// </summary>
    public void Teleport(Vector3 position, float yRotation)
    {
        cc.enabled = false;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0, yRotation, 0);
        currentPitch = 0f;
        cc.enabled = true;

        if (heldObject != null)
        {
            Destroy(heldObject);
            heldObject = null;
        }
    }

    // =========================================================================
    //  Action execution
    // =========================================================================

    private bool ExecuteAction(AgentAction action)
    {
        switch (action)
        {
            case AgentAction.MoveAhead:  return TryMove(transform.forward);
            case AgentAction.MoveBack:   return TryMove(-transform.forward);
            case AgentAction.MoveLeft:   return TryMove(-transform.right);
            case AgentAction.MoveRight:  return TryMove(transform.right);
            case AgentAction.RotateLeft:  Rotate(-rotateStep); return true;
            case AgentAction.RotateRight: Rotate(rotateStep);  return true;
            case AgentAction.LookUp:     return ChangePitch(-rotateStep);
            case AgentAction.LookDown:   return ChangePitch(rotateStep);
            case AgentAction.PickupObject: return TryPickup();
            case AgentAction.PutObject:    return TryPut();
            case AgentAction.OpenObject:   return TryOpen();
            case AgentAction.Done:       return true;
            default:                     return false;
        }
    }

    private bool TryMove(Vector3 direction)
    {
        Vector3 before = transform.position;

        // Pure horizontal movement — CharacterController handles wall collision
        Vector3 motion = direction.normalized * moveStep;
        motion.y = 0;
        cc.Move(motion);

        // Snap to ground after horizontal move
        if (!cc.isGrounded)
        {
            cc.Move(Vector3.down * 2f);
        }

        // Success = actually moved horizontally
        Vector3 delta = transform.position - before;
        delta.y = 0;
        return delta.magnitude > 0.01f;
    }

    private void Rotate(float degrees)
    {
        transform.Rotate(0, degrees, 0, Space.Self);
    }

    private bool ChangePitch(float deltaDegrees)
    {
        float newPitch = currentPitch + deltaDegrees;
        if (Mathf.Abs(newPitch) > maxPitch) return false;

        currentPitch = newPitch;
        ApplyPitchToCameras();
        return true;
    }

    private void ApplyPitchToCameras()
    {
        if (rgbCamera != null)
            rgbCamera.transform.localRotation = Quaternion.Euler(currentPitch, 0, 0);
        if (depthCamera != null)
            depthCamera.transform.localRotation = Quaternion.Euler(currentPitch, 0, 0);
    }

    // =========================================================================
    //  Interaction (placeholder implementations — expand as needed)
    // =========================================================================

    private bool TryPickup()
    {
        if (heldObject != null) return false; // already holding something

        if (!RaycastInteractable(out RaycastHit hit)) return false;

        Rigidbody rb = hit.collider.GetComponentInParent<Rigidbody>();
        if (rb == null) return false;

        heldObject = rb.gameObject;
        rb.isKinematic = true;
        heldObject.transform.SetParent(rgbCamera != null ? rgbCamera.transform : transform, true);
        heldObject.transform.localPosition = Vector3.forward * 0.5f;
        return true;
    }

    private bool TryPut()
    {
        if (heldObject == null) return false;

        heldObject.transform.SetParent(null, true);
        Rigidbody rb = heldObject.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;
        heldObject = null;
        return true;
    }

    private bool TryOpen()
    {
        // Placeholder: in the future this will toggle door hinge / drawer animation.
        // For now, just detect if there's an interactable in front.
        return RaycastInteractable(out _);
    }

    private bool RaycastInteractable(out RaycastHit hit)
    {
        Vector3 origin = rgbCamera != null ? rgbCamera.transform.position : transform.position + Vector3.up;
        Vector3 direction = rgbCamera != null ? rgbCamera.transform.forward : transform.forward;
        return Physics.Raycast(origin, direction, out hit, interactRange);
    }

    // =========================================================================
    //  Observation capture
    // =========================================================================

    private AgentObservation CaptureObservation()
    {
        AgentObservation obs = new()
        {
            position = transform.position,
            rotation = transform.eulerAngles,
            currentFloor = currentFloor,
            lastActionSuccess = lastActionSuccess,
            heldObject = heldObject != null ? heldObject.name : null
        };

        // RGB capture
        if (rgbCamera != null)
        {
            obs.rgb = CaptureCamera(rgbCamera, rgbRT, rgbTex);
        }

        // Depth capture
        if (depthCamera != null)
        {
            obs.depth = CaptureCamera(depthCamera, depthRT, depthTex);
        }

        return obs;
    }

    private Texture2D CaptureCamera(Camera cam, RenderTexture rt, Texture2D output)
    {
        Camera prev = Camera.current;
        RenderTexture prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        output.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        output.Apply();

        cam.targetTexture = null;
        RenderTexture.active = prevActive;

        return output;
    }

    // =========================================================================
    //  Debug: manual keyboard control for testing in editor
    // =========================================================================

    [Header("Debug")]
    public bool enableKeyboardControl = true;

    private void Update()
    {
        if (!enableKeyboardControl) return;

        AgentAction? action = null;

        if (Input.GetKeyDown(KeyCode.W))          action = AgentAction.MoveAhead;
        else if (Input.GetKeyDown(KeyCode.S))      action = AgentAction.MoveBack;
        else if (Input.GetKeyDown(KeyCode.A))      action = AgentAction.MoveLeft;
        else if (Input.GetKeyDown(KeyCode.D))      action = AgentAction.MoveRight;
        else if (Input.GetKeyDown(KeyCode.Q))      action = AgentAction.RotateLeft;
        else if (Input.GetKeyDown(KeyCode.E))      action = AgentAction.RotateRight;
        else if (Input.GetKeyDown(KeyCode.R))      action = AgentAction.LookUp;
        else if (Input.GetKeyDown(KeyCode.F))      action = AgentAction.LookDown;
        else if (Input.GetKeyDown(KeyCode.G))      action = AgentAction.PickupObject;
        else if (Input.GetKeyDown(KeyCode.H))      action = AgentAction.PutObject;
        else if (Input.GetKeyDown(KeyCode.T))      action = AgentAction.OpenObject;

        if (action.HasValue)
        {
            AgentObservation obs = Step(action.Value);
            Debug.Log($"[Agent] {action.Value} → success={obs.lastActionSuccess} pos=({obs.position.x:F2}, {obs.position.y:F2}, {obs.position.z:F2}) grounded={cc.isGrounded}");
        }
    }
}
