using System;
using System.Collections;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Connects Unity to an external scene generation service.
/// Flow: text requirement → HTTP POST → receive building JSON → write to StreamingAssets → rebuild scene.
///
/// Usage:
///   1. Attach to any GameObject.
///   2. Assign the MansionSceneBuilder reference.
///   3. Call GenerateFromPrompt() or use the built-in debug UI (press P in Play mode).
///
/// Expected server API:
///   POST /generate
///   Body: { "requirement": "...", "floors": 3 }
///   Response: { "building_name": "gen_xxx", "floors": { "floor_1.json": {...}, "floor_2.json": {...} } }
/// </summary>
public class SceneOrchestrator : MonoBehaviour
{
    [Header("References")]
    public MansionSceneBuilder builder;

    [Header("Server")]
    public string serverUrl = "http://localhost:5050/generate";
    public float timeoutSeconds = 120f;

    [Header("Debug")]
    public bool enableDebugUI = true;

    private bool isGenerating;
    private string statusMessage = "";

    /// <summary>
    /// Main API: generate a building from a text requirement and rebuild the scene.
    /// </summary>
    public void GenerateFromPrompt(string requirement, int floors = 3)
    {
        if (isGenerating)
        {
            Debug.LogWarning("[Orchestrator] Generation already in progress.");
            return;
        }
        StartCoroutine(RequestAndBuild(requirement, floors));
    }

    private IEnumerator RequestAndBuild(string requirement, int floorCount)
    {
        isGenerating = true;
        statusMessage = "Sending request to generation server...";
        Debug.Log($"[Orchestrator] Requesting: \"{requirement}\" ({floorCount} floors)");

        // Build request
        string body = JsonConvert.SerializeObject(new
        {
            requirement,
            floors = floorCount
        });

        using UnityWebRequest req = new(serverUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = (int)timeoutSeconds;

        statusMessage = "Waiting for server response...";
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            statusMessage = $"Error: {req.error}";
            Debug.LogError($"[Orchestrator] Request failed: {req.error}");
            isGenerating = false;
            yield break;
        }

        statusMessage = "Parsing response...";

        // Parse response
        JObject response;
        try
        {
            response = JObject.Parse(req.downloadHandler.text);
        }
        catch (JsonException ex)
        {
            statusMessage = $"Error: Invalid JSON response";
            Debug.LogError($"[Orchestrator] Failed to parse response: {ex.Message}");
            isGenerating = false;
            yield break;
        }

        // Extract building name and floor data
        string buildingName = response["building_name"]?.ToString()
            ?? $"gen_{DateTime.Now:yyyyMMdd_HHmmss}";
        JObject floorsData = response["floors"] as JObject;

        if (floorsData == null || floorsData.Count == 0)
        {
            statusMessage = "Error: No floor data in response";
            Debug.LogError("[Orchestrator] Response contains no floor data.");
            isGenerating = false;
            yield break;
        }

        // Write floor JSONs to StreamingAssets
        statusMessage = $"Writing {floorsData.Count} floors to disk...";
        string buildingDir = Path.Combine(Application.streamingAssetsPath, "Buildings", buildingName);
        Directory.CreateDirectory(buildingDir);

        foreach (var kv in floorsData)
        {
            string fileName = kv.Key;
            if (!fileName.EndsWith(".json")) fileName += ".json";
            string filePath = Path.Combine(buildingDir, fileName);
            File.WriteAllText(filePath, kv.Value.ToString());
        }

        Debug.Log($"[Orchestrator] Wrote {floorsData.Count} floors to {buildingDir}");

        // Rebuild scene
        statusMessage = "Rebuilding scene...";
        if (builder == null)
        {
            builder = FindFirstObjectByType<MansionSceneBuilder>();
        }

        if (builder != null)
        {
            yield return StartCoroutine(RebuildCoroutine(buildingName));
        }
        else
        {
            statusMessage = "Error: MansionSceneBuilder not found";
            Debug.LogError("[Orchestrator] No MansionSceneBuilder in scene.");
        }

        isGenerating = false;
    }

    private IEnumerator RebuildCoroutine(string buildingName)
    {
        var task = builder.RebuildFromFolder(buildingName);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            statusMessage = $"Error: {task.Exception?.InnerException?.Message}";
            Debug.LogError($"[Orchestrator] Rebuild failed: {task.Exception}");
        }
        else
        {
            statusMessage = $"Done! Loaded building: {buildingName}";
            Debug.Log($"[Orchestrator] Scene rebuilt: {buildingName}");
        }
    }

    // =========================================================================
    //  Convenience: load an existing local building by folder name
    // =========================================================================

    /// <summary>
    /// Load a building that already exists in StreamingAssets/Buildings/.
    /// No server call needed.
    /// </summary>
    public void LoadLocalBuilding(string buildingFolderName)
    {
        if (isGenerating)
        {
            Debug.LogWarning("[Orchestrator] Operation in progress.");
            return;
        }
        StartCoroutine(LoadLocalCoroutine(buildingFolderName));
    }

    private IEnumerator LoadLocalCoroutine(string buildingFolderName)
    {
        isGenerating = true;
        statusMessage = $"Loading {buildingFolderName}...";

        if (builder == null) builder = FindFirstObjectByType<MansionSceneBuilder>();
        if (builder != null)
        {
            yield return StartCoroutine(RebuildCoroutine(buildingFolderName));
        }

        isGenerating = false;
    }

    // =========================================================================
    //  Debug UI: press P to open a simple prompt input
    // =========================================================================

    private bool showUI;
    private string promptInput = "3-story office building with lobby, meeting rooms, and open workspace";
    private int floorInput = 3;

    private void Update()
    {
        if (enableDebugUI && Input.GetKeyDown(KeyCode.P))
        {
            showUI = !showUI;
        }
    }

    private void OnGUI()
    {
        if (!enableDebugUI) return;

        // Always show status bar when generating
        if (isGenerating)
        {
            GUI.Label(new Rect(10, 10, 600, 25), $"[Orchestrator] {statusMessage}");
        }

        if (!showUI) return;

        GUILayout.BeginArea(new Rect(10, 40, 500, 200));
        GUILayout.BeginVertical("box");

        GUILayout.Label("Scene Generation (press P to toggle)");

        GUILayout.Label("Requirement:");
        promptInput = GUILayout.TextField(promptInput);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Floors:", GUILayout.Width(50));
        string floorStr = GUILayout.TextField(floorInput.ToString(), GUILayout.Width(40));
        int.TryParse(floorStr, out floorInput);
        GUILayout.EndHorizontal();

        GUI.enabled = !isGenerating;
        if (GUILayout.Button(isGenerating ? "Generating..." : "Generate & Load"))
        {
            GenerateFromPrompt(promptInput, floorInput);
            showUI = false;
        }
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(statusMessage))
        {
            GUILayout.Label(statusMessage);
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
