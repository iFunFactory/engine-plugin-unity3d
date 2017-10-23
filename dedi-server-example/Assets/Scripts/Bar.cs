using UnityEngine;
using UnityEngine.EventSystems;


public class Bar : MonoBehaviour, IDragHandler
{
    public bool localPlayer;


    public void Ready ()
    {
        SetPosX(0f);
    }

    public void SetPosX (float px)
    {
        transform.localPosition = new Vector3(px, transform.localPosition.y);
    }

    public void OnDrag (PointerEventData eventData)
    {
        float px = transform.localPosition.x + eventData.delta.x * deltaScaler;
        px = Mathf.Max(Mathf.Min(px, kEndPosX), -kEndPosX);
        transform.localPosition = new Vector3(px, transform.localPosition.y);
    }

    void FixedUpdate ()
    {
        if (!localPlayer)
            return;

        if (Mathf.Abs(lastBarX - transform.localPosition.x) > 1)
        {
            lastBarX = transform.localPosition.x;

            PongMessage.SendBarPos(transform.localPosition.x, Time.realtimeSinceStartup);
        }
    }


    public static float deltaScaler = 1f;
    static float kEndPosX = 163f;

    float lastBarX = 0;
}
