using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using Newtonsoft.Json;
using UnityEngine;
using Unity.AI.Navigation;

public class MansionSceneBuilder : MonoBehaviour
{
    [Header("Path")]
    public string buildingFolder = "test_building";
    public int initialFloor = 1;
    public float floorHeight = 3.0f;

    [Header("Materials")]
    public Material wallMat;
    public Material floorMat;

    [Header("Runtime")]
    public bool buildNavMesh = true;
    public FloorManager floorManager;
    public bool useSemanticProxyFallback = true;
    public bool skipDoorwayPlaceholders = true;
    public bool logFurnitureStats = true;

    private readonly Dictionary<int, GameObject> floors = new();
    private readonly Dictionary<string, Mesh> meshCache = new();
    private readonly Dictionary<string, Material> materialCache = new();
    private Material proxyWoodMaterial;
    private Material proxyMetalMaterial;
    private Material proxySoftMaterial;
    private Material proxyDarkMaterial;
    private Material proxyLightMaterial;

    private async void Start()
    {
        string basePath = Path.Combine(Application.streamingAssetsPath, "Buildings", buildingFolder);
        if (!Directory.Exists(basePath))
        {
            Debug.LogError($"Building folder not found: {basePath}");
            return;
        }

        string[] files = Directory.GetFiles(basePath, "floor_*.json");
        Array.Sort(files, CompareFloorJsonPaths);

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(name.Replace("floor_", string.Empty), out int num))
            {
                Debug.LogWarning($"Skipped unexpected floor file: {file}");
                continue;
            }

            string json = File.ReadAllText(file);
            FloorData data;
            try
            {
                data = JsonConvert.DeserializeObject<FloorData>(json);
            }
            catch (JsonException ex)
            {
                Debug.LogError($"Failed to deserialize {file}: {ex.Message}");
                continue;
            }

            if (data == null)
            {
                Debug.LogWarning($"Failed to deserialize: {file}");
                continue;
            }

            GameObject root = new($"Floor_{num}");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.up * (num - 1) * floorHeight;
            floors[num] = root;

            if (data.rooms != null)
            {
                foreach (RoomData room in data.rooms)
                {
                    BuildFloorMesh(room, root.transform);
                }
            }

            if (data.walls != null)
            {
                foreach (WallData wall in data.walls)
                {
                    BuildWallMesh(wall, root.transform);
                }
            }

            if (data.objects != null)
            {
                await LoadFurniture(data.objects, root.transform, num);
            }

            if (buildNavMesh)
            {
                BuildFloorNavMesh(root);
            }

            root.SetActive(num == initialFloor);
        }

        if (floorManager == null)
        {
            floorManager = GetComponent<FloorManager>();
        }

        if (floorManager != null)
        {
            floorManager.SetFloors(floors, initialFloor);
        }

        Debug.Log($"Loaded {floors.Count} floors from {basePath}");
    }

    private void BuildFloorMesh(RoomData room, Transform parent)
    {
        if (room?.floorPolygon == null || room.floorPolygon.Count < 3)
        {
            return;
        }

        Mesh mesh = PolygonTriangulator.Triangulate(room.floorPolygon);
        if (mesh == null || mesh.vertexCount < 3)
        {
            Debug.LogWarning($"Failed to triangulate room floor: {room?.id}");
            return;
        }

        mesh.name = $"FloorMesh_{room.id}";
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new($"Floor_{room.id}");
        go.transform.SetParent(parent, false);

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = floorMat != null ? floorMat : DefaultFloorMaterial();
    }

    private void BuildWallMesh(WallData wall, Transform parent)
    {
        if (wall?.polygon == null || wall.polygon.Count < 4)
        {
            return;
        }

        List<Vector3> vertices = PolygonToVectors(wall.polygon);
        if (vertices.Count < 4)
        {
            return;
        }

        Mesh mesh = new() { name = $"WallMesh_{wall.id}" };
        Vector3[] quad = { vertices[0], vertices[1], vertices[2], vertices[3] };
        mesh.vertices = quad;
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new($"Wall_{wall.id}");
        go.transform.SetParent(parent, false);

        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = wallMat != null ? wallMat : DefaultWallMaterial();
    }

    private async Task LoadFurniture(List<ObjectPlacement> objects, Transform parent, int floorNumber)
    {
        string assetRoot = Path.Combine(Application.streamingAssetsPath, "ObjathorAssets");
        int glbLoaded = 0;
        int convertedLoaded = 0;
        int proxyLoaded = 0;
        int placeholderLoaded = 0;
        int skipped = 0;

        foreach (ObjectPlacement obj in objects)
        {
            if (obj?.assetId == null || obj.position == null)
            {
                continue;
            }

            if (skipDoorwayPlaceholders && IsDoorwayAsset(obj))
            {
                skipped++;
                continue;
            }

            string glbPath = Path.Combine(assetRoot, obj.assetId, $"{obj.assetId}.glb");
            string meshJsonPath = Path.Combine(assetRoot, obj.assetId, $"{obj.assetId}.mesh.json.gz");

            if (File.Exists(glbPath))
            {
                GameObject go = await LoadGLB(glbPath);
                if (go != null)
                {
                    ApplyObjectTransform(go, obj, parent);
                    glbLoaded++;
                    continue;
                }
            }

            if (File.Exists(meshJsonPath))
            {
                GameObject go = LoadConvertedObjathorMesh(meshJsonPath, obj.assetId);
                if (go != null)
                {
                    ApplyObjectTransform(go, obj, parent, extraYRotation: GetYRotOffset(meshJsonPath));
                    convertedLoaded++;
                    continue;
                }
            }

            if (useSemanticProxyFallback)
            {
                GameObject proxy = BuildSemanticProxy(obj);
                if (proxy != null)
                {
                    ApplyObjectTransform(proxy, obj, parent);
                    proxyLoaded++;
                    continue;
                }
            }

            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.transform.localScale = Vector3.one * 0.3f;
            placeholder.GetComponent<MeshRenderer>().sharedMaterial = ProxyDarkMaterial();
            ApplyObjectTransform(placeholder, obj, parent);
            placeholderLoaded++;
        }

        if (logFurnitureStats)
        {
            Debug.Log($"Floor {floorNumber}: GLB={glbLoaded}, Converted={convertedLoaded}, Proxy={proxyLoaded}, Placeholder={placeholderLoaded}, Skipped={skipped}");
        }
    }

    private async Task<GameObject> LoadGLB(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        GltfImport gltf = new();
        bool ok = await gltf.Load(data, new Uri(path));
        if (!ok)
        {
            Debug.LogWarning($"Failed to load GLB: {path}");
            return null;
        }

        GameObject go = new(Path.GetFileNameWithoutExtension(path));
        bool instantiated = await gltf.InstantiateMainSceneAsync(go.transform);
        if (!instantiated)
        {
            Destroy(go);
            Debug.LogWarning($"Failed to instantiate GLB scene: {path}");
            return null;
        }

        return go;
    }

    private void ApplyObjectTransform(GameObject go, ObjectPlacement obj, Transform parent, float extraYRotation = 0f)
    {
        go.name = string.IsNullOrEmpty(obj.id) ? obj.assetId : obj.id;
        go.transform.SetParent(parent, false);
        Vector3 euler = obj.rotation != null ? obj.rotation.ToUnity() : Vector3.zero;
        euler.y += extraYRotation;
        go.transform.localEulerAngles = euler;
        Vector3 position = obj.position.ToUnity();
        position.y -= EstimateLocalModelCenterY(go);
        go.transform.localPosition = position;
    }

    private GameObject LoadConvertedObjathorMesh(string path, string assetId)
    {
        if (!meshCache.TryGetValue(assetId, out Mesh mesh) || mesh == null)
        {
            ObjathorMeshData data = ReadMeshData(path);
            if (data == null || data.vertices == null || data.triangles == null)
            {
                return null;
            }

            mesh = BuildMesh(data);
            meshCache[assetId] = mesh;
        }

        if (!materialCache.TryGetValue(assetId, out Material material) || material == null)
        {
            ObjathorMeshData data = ReadMeshData(path);
            material = BuildMaterial(data, Path.GetDirectoryName(path));
            materialCache[assetId] = material;
        }

        GameObject go = new(assetId);
        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return go;
    }

    private ObjathorMeshData ReadMeshData(string path)
    {
        using FileStream fs = File.OpenRead(path);
        using GZipStream gz = new(fs, CompressionMode.Decompress);
        using StreamReader reader = new(gz);
        string json = reader.ReadToEnd();
        return JsonConvert.DeserializeObject<ObjathorMeshData>(json);
    }

    private float GetYRotOffset(string path)
    {
        ObjathorMeshData data = ReadMeshData(path);
        return data?.yRotOffset ?? 0f;
    }

    private Mesh BuildMesh(ObjathorMeshData data)
    {
        Mesh mesh = new();

        int vertexCount = data.vertices.Length / 3;
        Vector3[] vertices = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3(
                data.vertices[i * 3 + 0],
                data.vertices[i * 3 + 1],
                data.vertices[i * 3 + 2]
            );
        }

        mesh.vertices = vertices;
        mesh.triangles = data.triangles;

        if (data.normals != null && data.normals.Length == data.vertices.Length)
        {
            Vector3[] normals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                normals[i] = new Vector3(
                    data.normals[i * 3 + 0],
                    data.normals[i * 3 + 1],
                    data.normals[i * 3 + 2]
                );
            }
            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }

        if (data.uvs != null && data.uvs.Length == vertexCount * 2)
        {
            Vector2[] uvs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                uvs[i] = new Vector2(
                    data.uvs[i * 2 + 0],
                    data.uvs[i * 2 + 1]
                );
            }
            mesh.uv = uvs;
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    private Material BuildMaterial(ObjathorMeshData data, string assetDir)
    {
        Material material = new(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = Color.white;

        if (!string.IsNullOrEmpty(data?.albedo))
        {
            string texPath = Path.Combine(assetDir, data.albedo);
            Texture2D tex = LoadTexture(texPath);
            if (tex != null)
            {
                material.SetTexture("_BaseMap", tex);
                material.mainTexture = tex;
            }
        }

        return material;
    }

    private Texture2D LoadTexture(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D tex = new(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(bytes);
        tex.name = Path.GetFileName(path);
        return tex;
    }

    private void BuildFloorNavMesh(GameObject floorRoot)
    {
        if (floorRoot == null)
        {
            return;
        }

        NavMeshSurface surface = floorRoot.GetComponent<NavMeshSurface>();
        if (surface == null)
        {
            surface = floorRoot.AddComponent<NavMeshSurface>();
        }

        surface.collectObjects = CollectObjects.Children;
        surface.BuildNavMesh();
    }

    private bool IsDoorwayAsset(ObjectPlacement obj)
    {
        string key = BuildSemanticKey(obj);
        return key.Contains("doorway");
    }

    private GameObject BuildSemanticProxy(ObjectPlacement obj)
    {
        string key = BuildSemanticKey(obj);

        if (key.Contains("laptop"))
        {
            return BuildLaptopProxy(obj.assetId);
        }

        if (key.Contains("office_chair") || key.Contains("chair") || key.Contains("stool"))
        {
            return BuildChairProxy(obj.assetId, key.Contains("stool"));
        }

        if (key.Contains("sofa"))
        {
            return BuildSofaProxy(obj.assetId);
        }

        if (key.Contains("dining_table") || key.Contains("meeting_table"))
        {
            return BuildRoundTableProxy(obj.assetId);
        }

        if (key.Contains("desk") || key.Contains("table") || key.Contains("counter"))
        {
            return BuildRectTableProxy(obj.assetId, key.Contains("counter"));
        }

        if (key.Contains("bin"))
        {
            return BuildBinProxy(obj.assetId);
        }

        if (key.Contains("toilet"))
        {
            return BuildToiletProxy(obj.assetId);
        }

        if (key.Contains("stair"))
        {
            return BuildStairProxy(obj.assetId);
        }

        return null;
    }

    private string BuildSemanticKey(ObjectPlacement obj)
    {
        string a = obj.assetId ?? string.Empty;
        string b = obj.object_name ?? string.Empty;
        string c = obj.id ?? string.Empty;
        return $"{a} {b} {c}".ToLowerInvariant();
    }

    private GameObject BuildLaptopProxy(string name)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.015f, 0f), new Vector3(0.32f, 0.02f, 0.22f), ProxyDarkMaterial());
        GameObject screen = CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.14f, -0.09f), new Vector3(0.30f, 0.18f, 0.015f), ProxyLightMaterial());
        screen.transform.localRotation = Quaternion.Euler(-100f, 0f, 0f);
        return root;
    }

    private GameObject BuildChairProxy(string name, bool stool)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, stool ? 0.42f : 0.46f, 0f), new Vector3(0.42f, 0.05f, 0.42f), ProxySoftMaterial());
        if (!stool)
        {
            CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.82f, -0.17f), new Vector3(0.42f, 0.42f, 0.06f), ProxySoftMaterial());
        }
        float legHeight = stool ? 0.42f : 0.46f;
        Vector3 legScale = new(0.05f, legHeight, 0.05f);
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(-0.16f, legHeight * 0.5f, -0.16f), legScale, ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0.16f, legHeight * 0.5f, -0.16f), legScale, ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(-0.16f, legHeight * 0.5f, 0.16f), legScale, ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0.16f, legHeight * 0.5f, 0.16f), legScale, ProxyMetalMaterial());
        return root;
    }

    private GameObject BuildRectTableProxy(string name, bool counter)
    {
        GameObject root = new(name + "_proxy");
        Vector3 topSize = counter ? new Vector3(2.0f, 0.08f, 0.60f) : new Vector3(1.60f, 0.08f, 0.80f);
        float topY = counter ? 1.00f : 0.74f;
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, topY, 0f), topSize, ProxyWoodMaterial());
        float legHeight = topY * 0.5f;
        Vector3 legScale = new(0.08f, topY, 0.08f);
        float dx = topSize.x * 0.42f;
        float dz = topSize.z * 0.42f;
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(-dx, legHeight, -dz), legScale, ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(dx, legHeight, -dz), legScale, ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(-dx, legHeight, dz), legScale, ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(dx, legHeight, dz), legScale, ProxyMetalMaterial());
        return root;
    }

    private GameObject BuildRoundTableProxy(string name)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.73f, 0f), new Vector3(0.65f, 0.04f, 0.65f), ProxyWoodMaterial());
        CreatePrimitiveChild(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.37f, 0f), new Vector3(0.09f, 0.37f, 0.09f), ProxyMetalMaterial());
        CreatePrimitiveChild(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(0.35f, 0.03f, 0.35f), ProxyMetalMaterial());
        return root;
    }

    private GameObject BuildSofaProxy(string name)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.22f, 0f), new Vector3(1.80f, 0.24f, 0.82f), ProxySoftMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.62f, -0.28f), new Vector3(1.80f, 0.56f, 0.18f), ProxySoftMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(-0.82f, 0.42f, 0f), new Vector3(0.16f, 0.44f, 0.82f), ProxySoftMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0.82f, 0.42f, 0f), new Vector3(0.16f, 0.44f, 0.82f), ProxySoftMaterial());
        return root;
    }

    private GameObject BuildBinProxy(string name)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.20f, 0f), new Vector3(0.16f, 0.20f, 0.16f), ProxyDarkMaterial());
        return root;
    }

    private GameObject BuildToiletProxy(string name)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.18f, 0.08f), new Vector3(0.42f, 0.28f, 0.52f), ProxyLightMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.52f, -0.12f), new Vector3(0.42f, 0.42f, 0.14f), ProxyLightMaterial());
        return root;
    }

    private GameObject BuildStairProxy(string name)
    {
        GameObject root = new(name + "_proxy");
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.10f, -0.24f), new Vector3(1.20f, 0.20f, 0.28f), ProxyConcreteMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.30f, 0f), new Vector3(1.20f, 0.20f, 0.28f), ProxyConcreteMaterial());
        CreatePrimitiveChild(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.50f, 0.24f), new Vector3(1.20f, 0.20f, 0.28f), ProxyConcreteMaterial());
        return root;
    }

    private GameObject CreatePrimitiveChild(PrimitiveType type, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject child = GameObject.CreatePrimitive(type);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        child.transform.localScale = localScale;
        MeshRenderer renderer = child.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        Destroy(child.GetComponent<Collider>());
        return child;
    }

    private float EstimateLocalModelCenterY(GameObject go)
    {
        MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        Bounds bounds = default;

        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null)
            {
                continue;
            }

            Bounds meshBounds = filter.sharedMesh.bounds;
            Vector3 center = filter.transform.localPosition + filter.transform.localRotation * Vector3.Scale(meshBounds.center, filter.transform.localScale);
            Vector3 extents = Vector3.Scale(meshBounds.extents, Abs(filter.transform.localScale));
            Bounds worldLike = new(center, extents * 2f);

            if (!hasBounds)
            {
                bounds = worldLike;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(worldLike.min);
                bounds.Encapsulate(worldLike.max);
            }
        }

        return hasBounds ? bounds.center.y : 0f;
    }

    private static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    private Material ProxyWoodMaterial()
    {
        if (proxyWoodMaterial == null)
        {
            proxyWoodMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            proxyWoodMaterial.color = new Color(0.56f, 0.38f, 0.22f, 1f);
        }
        return proxyWoodMaterial;
    }

    private Material ProxyMetalMaterial()
    {
        if (proxyMetalMaterial == null)
        {
            proxyMetalMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            proxyMetalMaterial.color = new Color(0.45f, 0.48f, 0.52f, 1f);
        }
        return proxyMetalMaterial;
    }

    private Material ProxySoftMaterial()
    {
        if (proxySoftMaterial == null)
        {
            proxySoftMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            proxySoftMaterial.color = new Color(0.33f, 0.45f, 0.62f, 1f);
        }
        return proxySoftMaterial;
    }

    private Material ProxyDarkMaterial()
    {
        if (proxyDarkMaterial == null)
        {
            proxyDarkMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            proxyDarkMaterial.color = new Color(0.18f, 0.20f, 0.23f, 1f);
        }
        return proxyDarkMaterial;
    }

    private Material ProxyLightMaterial()
    {
        if (proxyLightMaterial == null)
        {
            proxyLightMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            proxyLightMaterial.color = new Color(0.88f, 0.89f, 0.90f, 1f);
        }
        return proxyLightMaterial;
    }

    private Material ProxyConcreteMaterial()
    {
        return DefaultWallMaterial();
    }

    private static int CompareFloorJsonPaths(string a, string b)
    {
        int floorA = ExtractFloorNumber(a);
        int floorB = ExtractFloorNumber(b);
        return floorA.CompareTo(floorB);
    }

    private static int ExtractFloorNumber(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path).Replace("floor_", string.Empty);
        return int.TryParse(name, out int number) ? number : int.MaxValue;
    }

    private static List<Vector3> PolygonToVectors(List<Vec3> polygon)
    {
        List<Vector3> vertices = new();
        foreach (Vec3 point in polygon)
        {
            if (point == null)
            {
                continue;
            }
            vertices.Add(point.ToUnity());
        }
        return vertices;
    }

    private Material DefaultWallMaterial()
    {
        Material material = new(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(0.82f, 0.82f, 0.82f, 1f);
        return material;
    }

    private Material DefaultFloorMaterial()
    {
        Material material = new(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = new Color(0.55f, 0.45f, 0.32f, 1f);
        return material;
    }
}
