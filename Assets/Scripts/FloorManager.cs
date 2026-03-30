using System.Collections.Generic;
using UnityEngine;

public class FloorManager : MonoBehaviour
{
    public int currentFloor = 1;

    private Dictionary<int, GameObject> floors = new();

    public void SetFloors(Dictionary<int, GameObject> newFloors, int initial)
    {
        floors = newFloors != null ? new Dictionary<int, GameObject>(newFloors) : new Dictionary<int, GameObject>();
        currentFloor = initial;

        foreach (KeyValuePair<int, GameObject> pair in floors)
        {
            if (pair.Value != null)
            {
                pair.Value.SetActive(pair.Key == currentFloor);
            }
        }
    }

    public void SwitchTo(int target)
    {
        if (target == currentFloor)
        {
            return;
        }

        if (floors.TryGetValue(currentFloor, out GameObject oldFloor) && oldFloor != null)
        {
            oldFloor.SetActive(false);
        }

        if (floors.TryGetValue(target, out GameObject nextFloor) && nextFloor != null)
        {
            nextFloor.SetActive(true);
            currentFloor = target;
        }
    }

    public void SwitchRelative(int delta)
    {
        SwitchTo(currentFloor + delta);
    }
}
