using UnityEngine;

using SimpleJSON;
using Fun;

public class FunapiNetworkTester : MonoBehaviour {

    // Use this for initialization
    public void Start()
    {
        UnityEngine.Debug.Log("Creating a network instance.");
        // You should pass an instance of FunapiTransport.
        // Currently only FunapiTcpTransport is supported.
        network_ = new FunapiNetwork(new FunapiTcpTransport("192.168.244.151", 8012), this.OnSessionInitiated, this.OnSessionClosed);
    }

    // Update is called once per frame
    public void Update()
    {
        if (start_time_ != 0.0f)
        {
            if (start_time_ + 5.0f < Time.time)
            {
                if (network_.Connected == false)
                {
                    UnityEngine.Debug.Log("Failed to make a connection. Maybe the server is down? Stopping the network module.");
                    network_.Stop();
                }
                else
                {
                    UnityEngine.Debug.Log("Seems network succeeded to make a connection to a server.");
                }
                start_time_ = 0.0f;
            }
        }
    }

    public void OnGUI()
    {
        // For debugging
        if (GUI.Button(new Rect(10, 30, 120, 20), "Connect"))
        {
            if (network_.Started)
            {
                UnityEngine.Debug.Log("Already connected. Disconnect first.");
            }
            else
            {
                network_.RegisterHandler("echo", this.OnEcho);
                start_time_ = Time.time;
                network_.Start();
            }
        }
        if (GUI.Button(new Rect(10, 60, 120, 20), "Disconnect"))
        {
            if (network_.Started == false)
            {
                UnityEngine.Debug.Log("You should connect first.");
            }
            else
            {
                network_.Stop();
            }
        }
        if (GUI.Button(new Rect(10, 90, 120, 20), "Send 'Hello World'"))
        {
            if (network_.Started == false)
            {
                UnityEngine.Debug.Log("You should connect first.");
            }
            else
            {
                JSONClass example = new JSONClass();
                example["message"] = "hello world";
                network_.SendMessage("echo", example);
            }
        }
    }

    private void OnSessionInitiated(string session_id)
    {
        UnityEngine.Debug.Log("Session initiated. Session id:" + session_id);
    }

    private void OnSessionClosed()
    {
        UnityEngine.Debug.Log("Session closed");
    }

    private void OnEcho(string msg_type, JSONClass body)
    {
        UnityEngine.Debug.Log("Received an echo message: " + body.ToString());
    }

    private FunapiNetwork network_;
    private float start_time_ = 0.0f;

    // Another Funapi-specific features will go here...
}
