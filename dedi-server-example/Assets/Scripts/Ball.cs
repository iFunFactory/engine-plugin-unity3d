using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;


public class Ball : MonoBehaviour
{
    void Awake ()
    {
        rigidBody_ = GetComponent<Rigidbody2D>();
        collider_ = GetComponent<CircleCollider2D>();
    }

    void FixedUpdate ()
    {
        if (isHost)
            SendProperties();
    }

    public void Reset ()
    {
        transform.localPosition = Vector3.zero;
        rigidBody_.velocity = Vector2.zero;
    }

    public void Incapacitation ()
    {
        if (rigidBody_ != null)
            rigidBody_.simulated = false;

        if (collider_ != null)
            collider_.enabled = false;
    }

    public void SetPos (float x, float y)
    {
        transform.localPosition = new Vector3(x, y);
    }

    public void SetVelocity (float x, float y)
    {
        if (rigidBody_ != null)
            rigidBody_.velocity = new Vector2(x, y);
    }

    public void SendProperties ()
    {
        PongMessage.SendBallPos(transform.localPosition.x, transform.localPosition.y,
                                rigidBody_.velocity.x, rigidBody_.velocity.y);
    }

    public bool isHost { private get; set; }


    Rigidbody2D rigidBody_;
    CircleCollider2D collider_;
}
