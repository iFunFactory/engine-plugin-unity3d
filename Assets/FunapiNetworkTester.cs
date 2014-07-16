using UnityEngine;

using SimpleJSON;
using Fun;

public class FunapiNetworkTester : MonoBehaviour
{
    // Update is called once per frame
    public void Update()
    {
        if (start_time_ != 0.0f)
        {
            if (start_time_ + 5.0f < Time.time)
            {
                if (network_ == null)
                {
                    UnityEngine.Debug.Log("Failed to make a connection. Network instance was not generated.");
                }
                else if (network_.Connected == false)
                {
                    UnityEngine.Debug.Log("Failed to make a connection. Maybe the server is down? Stopping the network module.");
                    network_.Stop();
                    network_ = null;
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
        GUI.enabled = network_ == null;
        if (GUI.Button(new Rect(30, 30, 120, 20), "Connect (TCP)"))
        {
            Connect(new FunapiTcpTransport(IP, 8012));
        }
        if (GUI.Button(new Rect(30, 60, 120, 20), "Connect (UDP)"))
        {
            Connect(new FunapiUdpTransport(IP, 8013));
            SendEchoMessage();
        }

        GUI.enabled = false;
        if (GUI.Button(new Rect(30, 90, 120, 20), "Connect (HTTP)"))
        {
            //Connect(new FunapiHttpTransport(IP, 8018));
            //SendEchoMessage();
        }

        GUI.enabled = network_ != null;
        if (GUI.Button(new Rect(30, 120, 120, 20), "Disconnect"))
        {
            DisConnect();
        }
        if (GUI.Button(new Rect(30, 150, 120, 20), "Send 'Hello World'"))
        {
            SendEchoMessage();
        }
    }

    private void Connect (FunapiTransport transport)
    {
        UnityEngine.Debug.Log("Creating a network instance.");
        // You should pass an instance of FunapiTransport.
        network_ = new FunapiNetwork(transport, this.OnSessionInitiated, this.OnSessionClosed);

        network_.RegisterHandler("echo", this.OnEcho);
        start_time_ = Time.time;
        network_.Start();
    }

    private void DisConnect ()
    {
        start_time_ = 0.0f;

        if (network_.Started == false)
        {
            UnityEngine.Debug.Log("You should connect first.");
        }
        else
        {
            network_.Stop();
            network_ = null;
        }
    }

    private void SendEchoMessage ()
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


    // Please change IP for test.
    private const string IP = "192.168.35.129";

    // member variables.
    private FunapiNetwork network_ = null;
    private float start_time_ = 0.0f;

    // Another Funapi-specific features will go here...
}
