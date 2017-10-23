using UnityEngine;

public class ScreenOrganizer : MonoBehaviour
{
    void Awake ()
    {
        Screen.SetResolution(300, 440,false);
    }
}
