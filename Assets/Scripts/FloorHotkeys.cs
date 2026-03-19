using UnityEngine;

public class FloorHotkeys : MonoBehaviour
{
    public FloorManager floorManager;
    public int minFloor = 1;
    public int maxFloor = 9;

    private void Awake()
    {
        if (floorManager == null)
        {
            floorManager = GetComponent<FloorManager>();
        }

        if (floorManager == null)
        {
            floorManager = FindAnyObjectByType<FloorManager>();
        }
    }

    private void Update()
    {
        if (floorManager == null)
        {
            return;
        }

        for (int floor = minFloor; floor <= maxFloor; floor++)
        {
            if (IsFloorPressed(floor))
            {
                floorManager.SwitchTo(floor);
                return;
            }
        }
    }

    private static bool IsFloorPressed(int floor)
    {
        return floor switch
        {
            1 => Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1),
            2 => Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2),
            3 => Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3),
            4 => Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4),
            5 => Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5),
            6 => Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6),
            7 => Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7),
            8 => Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8),
            9 => Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9),
            _ => false,
        };
    }
}
