using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

[Serializable]
public class FloorData
{
    public List<WallData> walls;
    public List<RoomData> rooms;
    public List<ObjectPlacement> objects;
    public JToken doors;
    public JToken windows;
    public JToken proceduralParameters;

    // Common extra fields present in the current MANSION sample JSON.
    public JToken floor_objects;
    public JToken wall_objects;
    public JToken small_objects;
    public JToken selected_objects;

    public JToken open_walls;
    public JToken open_room_pairs;
    public JToken room_pairs;

    public string skybox;
    public string timeOfDay;
    public float wall_height;
    public int portable_floor_number;

    public JToken agentPoses;
    public JToken metadata;
    public JToken receptacle2small_objects;
    public JToken raw_doorway_plan;
    public JToken raw_window_plan;
    public JToken wall_object_constraint_plan;
    public JToken portable_lock_open_walls;
}

[Serializable]
public class WallData
{
    public string id;
    public string roomId;
    public List<Vec3> polygon;
    public float height;
    public float width;
    public string direction;
    public string layer;
    public JToken segment;
    public JToken connect_exterior;
    public JToken connected_rooms;
    public JToken material;
}

[Serializable]
public class RoomData
{
    public string id;
    public string roomType;
    public string layer;
    public string floor_design;
    public string wall_design;
    public string portable_source_id;
    public string portable_open_relation;
    public List<Vec3> floorPolygon;
    public JToken vertices;
    public JToken full_vertices;
    public JToken ceilings;
    public JToken children;
    public JToken floorMaterial;
    public JToken wallMaterial;
    public JToken debug_door_window_placements;
}

[Serializable]
public class ObjectPlacement
{
    public string assetId;
    public string id;
    public string roomId;
    public string layer;
    public string object_name;
    public bool kinematic;
    public Vec3 position;
    public Vec3 rotation;
    public JToken material;
    public JToken vertices;
}

[Serializable]
public class ColorRgb
{
    public float r;
    public float g;
    public float b;

    public Color ToUnity() => new(r, g, b, 1f);
}

[Serializable]
public class Vec3
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToUnity() => new(x, y, z);
}
