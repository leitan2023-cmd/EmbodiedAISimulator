using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Records per-step observations from an EmbodiedAgentController into a structured
/// episode directory: RGB PNGs + trajectory JSON.
///
/// Usage:
///   1. Attach to any GameObject in the scene.
///   2. Assign the agent reference.
///   3. Call BeginEpisode() at start, RecordStep() each step, EndEpisode() when done.
///   4. Or enable autoRecord to automatically record every keyboard-driven step.
///
/// Output structure:
///   StreamingAssets/Episodes/ep_YYYYMMDD_HHmmss_fff/
///     meta.json
///     rgb/step_0000.png
///     rgb/step_0001.png
///     depth/step_0000.png    (if depth camera available)
///     trajectory.json
/// </summary>
public class ObservationRecorder : MonoBehaviour
{
    [Header("References")]
    public EmbodiedAgentController agent;

    [Header("Output")]
    public string outputRoot = "Episodes";

    [Header("Options")]
    public bool saveRGB = true;
    public bool saveDepth = true;

    private string episodeDir;
    private int stepCount;
    private List<StepRecord> trajectory;
    private EpisodeMeta currentMeta;
    private bool recording;

    /// <summary>
    /// Start recording a new episode.
    /// </summary>
    public void BeginEpisode(string taskType = "unspecified", string buildingName = "unknown",
        string targetObject = null)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string id = $"ep_{timestamp}";
        episodeDir = Path.Combine(Application.streamingAssetsPath, outputRoot, id);

        Directory.CreateDirectory(episodeDir);
        if (saveRGB) Directory.CreateDirectory(Path.Combine(episodeDir, "rgb"));
        if (saveDepth) Directory.CreateDirectory(Path.Combine(episodeDir, "depth"));

        stepCount = 0;
        trajectory = new List<StepRecord>();
        recording = true;

        currentMeta = new EpisodeMeta
        {
            episodeId = id,
            taskType = taskType,
            buildingName = buildingName,
            targetObject = targetObject,
            startTime = DateTime.Now.ToString("o"),
            agentStartPosition = Vec3From(agent.transform.position),
            agentStartRotation = Vec3From(agent.transform.eulerAngles),
            imageWidth = agent.imageWidth,
            imageHeight = agent.imageHeight
        };

        Debug.Log($"[Recorder] Episode started: {episodeDir}");
    }

    /// <summary>
    /// Record one step. Call this after each agent.Step() call.
    /// </summary>
    public void RecordStep(AgentAction action, AgentObservation obs, float reward = 0f,
        bool done = false, bool success = false)
    {
        if (!recording)
        {
            Debug.LogWarning("[Recorder] RecordStep called but no episode is active.");
            return;
        }

        string stepName = $"step_{stepCount:D4}";

        // Save RGB
        if (saveRGB && obs.rgb != null)
        {
            byte[] png = obs.rgb.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(episodeDir, "rgb", $"{stepName}.png"), png);
        }

        // Save Depth
        if (saveDepth && obs.depth != null)
        {
            byte[] png = obs.depth.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(episodeDir, "depth", $"{stepName}.png"), png);
        }

        // Append trajectory record
        trajectory.Add(new StepRecord
        {
            step = stepCount,
            action = action.ToString(),
            actionSuccess = obs.lastActionSuccess,
            reward = reward,
            done = done,
            success = success,
            position = Vec3From(obs.position),
            rotation = Vec3From(obs.rotation),
            floor = obs.currentFloor,
            heldObject = obs.heldObject
        });

        stepCount++;

        if (done)
        {
            EndEpisode(success);
        }
    }

    /// <summary>
    /// Finish the current episode and write meta + trajectory to disk.
    /// </summary>
    public void EndEpisode(bool success = false)
    {
        if (!recording) return;
        recording = false;

        currentMeta.endTime = DateTime.Now.ToString("o");
        currentMeta.totalSteps = stepCount;
        currentMeta.success = success;

        // Write meta.json
        string metaJson = JsonConvert.SerializeObject(currentMeta, Formatting.Indented);
        File.WriteAllText(Path.Combine(episodeDir, "meta.json"), metaJson);

        // Write trajectory.json
        string trajJson = JsonConvert.SerializeObject(trajectory, Formatting.Indented);
        File.WriteAllText(Path.Combine(episodeDir, "trajectory.json"), trajJson);

        Debug.Log($"[Recorder] Episode ended: {stepCount} steps, success={success} → {episodeDir}");
    }

    /// <summary>Is an episode currently being recorded?</summary>
    public bool IsRecording => recording;

    // =========================================================================
    //  Convenience: auto-record keyboard-driven steps
    // =========================================================================

    [Header("Auto Record")]
    [Tooltip("When enabled, automatically starts recording on Play and records all keyboard steps.")]
    public bool autoRecord;

    private AgentAction? pendingAction;

    private void Start()
    {
        if (agent == null)
        {
            agent = FindFirstObjectByType<EmbodiedAgentController>();
        }

        if (autoRecord && agent != null)
        {
            BeginEpisode("keyboard_debug");
        }
    }

    private void Update()
    {
        if (!autoRecord || !recording || agent == null) return;
        if (!agent.enableKeyboardControl) return;

        // Mirror the same key bindings as EmbodiedAgentController.Update()
        AgentAction? action = null;

        if (Input.GetKeyDown(KeyCode.W))          action = AgentAction.MoveAhead;
        else if (Input.GetKeyDown(KeyCode.S))      action = AgentAction.MoveBack;
        else if (Input.GetKeyDown(KeyCode.A))      action = AgentAction.MoveLeft;
        else if (Input.GetKeyDown(KeyCode.D))      action = AgentAction.MoveRight;
        else if (Input.GetKeyDown(KeyCode.Q))      action = AgentAction.RotateLeft;
        else if (Input.GetKeyDown(KeyCode.E))      action = AgentAction.RotateRight;
        else if (Input.GetKeyDown(KeyCode.R))      action = AgentAction.LookUp;
        else if (Input.GetKeyDown(KeyCode.F))      action = AgentAction.LookDown;

        if (action.HasValue)
        {
            // Agent already processes this key in its own Update().
            // We just capture the observation after it acts.
            // Use LateUpdate to ensure agent has already moved.
            pendingAction = action.Value;
        }
    }

    private void LateUpdate()
    {
        if (pendingAction.HasValue && recording && agent != null)
        {
            AgentObservation obs = agent.Observe();
            RecordStep(pendingAction.Value, obs);
            pendingAction = null;
        }
    }

    private void OnApplicationQuit()
    {
        if (recording) EndEpisode(false);
    }

    private void OnDestroy()
    {
        if (recording) EndEpisode(false);
    }

    // =========================================================================
    //  Data classes
    // =========================================================================

    private static float[] Vec3From(Vector3 v) => new[] { v.x, v.y, v.z };

    [Serializable]
    private class EpisodeMeta
    {
        public string episodeId;
        public string taskType;
        public string buildingName;
        public string targetObject;
        public string startTime;
        public string endTime;
        public float[] agentStartPosition;
        public float[] agentStartRotation;
        public int imageWidth;
        public int imageHeight;
        public int totalSteps;
        public bool success;
    }

    [Serializable]
    private class StepRecord
    {
        public int step;
        public string action;
        public bool actionSuccess;
        public float reward;
        public bool done;
        public bool success;
        public float[] position;
        public float[] rotation;
        public int floor;
        public string heldObject;
    }
}
