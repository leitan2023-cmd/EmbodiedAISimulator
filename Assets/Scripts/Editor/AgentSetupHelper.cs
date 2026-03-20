using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor menu item to create a fully configured EmbodiedAgent in the scene.
/// Menu: GameObject → Embodied AI → Create Agent
/// </summary>
public static class AgentSetupHelper
{
    [MenuItem("GameObject/Embodied AI/Create Agent", false, 10)]
    public static void CreateAgent()
    {
        // Root object
        GameObject agent = new("EmbodiedAgent");
        Undo.RegisterCreatedObjectUndo(agent, "Create Embodied Agent");

        // Default to center of F1_hub_room in test_building.
        // Adjust as needed for other buildings.
        agent.transform.position = new Vector3(10f, 0.5f, 7f);

        // CharacterController
        CharacterController cc = agent.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.2f;
        cc.center = new Vector3(0, 0.9f, 0);
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.3f;

        // RGB Camera
        GameObject rgbCamGO = new("RGBCamera");
        rgbCamGO.transform.SetParent(agent.transform, false);
        rgbCamGO.transform.localPosition = new Vector3(0, 1.6f, 0); // eye height
        Camera rgbCam = rgbCamGO.AddComponent<Camera>();
        rgbCam.fieldOfView = 90f;
        rgbCam.nearClipPlane = 0.05f;
        rgbCam.farClipPlane = 20f;
        // Disable auto-rendering — agent controls when to render
        rgbCam.enabled = false;

        // Depth Camera (co-located with RGB)
        GameObject depthCamGO = new("DepthCamera");
        depthCamGO.transform.SetParent(agent.transform, false);
        depthCamGO.transform.localPosition = new Vector3(0, 1.6f, 0);
        Camera depthCam = depthCamGO.AddComponent<Camera>();
        depthCam.fieldOfView = 90f;
        depthCam.nearClipPlane = 0.05f;
        depthCam.farClipPlane = 10f;
        depthCam.enabled = false;

        // Agent controller
        EmbodiedAgentController controller = agent.AddComponent<EmbodiedAgentController>();
        controller.rgbCamera = rgbCam;
        controller.depthCamera = depthCam;

        Selection.activeGameObject = agent;
        Debug.Log("[AgentSetup] Created EmbodiedAgent with RGB + Depth cameras. Keyboard debug: WASD=move, QE=rotate, RF=look up/down.");
    }
}
