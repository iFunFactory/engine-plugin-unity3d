// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using UnityEngine;


public class FunapiNetworkTest : MonoBehaviour
{
    public void OnGUI()
    {
        with_session_reliability_ = GUI.Toggle(new Rect(30, 38, 250, 20),
                                               with_session_reliability_,
                                               " use session reliability (only tcp)");

        with_protobuf_ = GUI.Toggle(new Rect(30, 58, 150, 20), with_protobuf_, " use protobuf-net");

        GUI.Label(new Rect(30, 8, 300, 20), "Server - " + kServerIp);
        GUI.enabled = (network_ == null || !network_.Started);
        if (GUI.Button(new Rect(30, 85, 240, 40), "Connect (TCP)"))
        {
            Connect(TransportProtocol.kTcp);
        }
        if (GUI.Button(new Rect(30, 130, 240, 40), "Connect (UDP)"))
        {
            Connect(TransportProtocol.kUdp);
        }
        if (GUI.Button(new Rect(30, 175, 240, 40), "Connect (HTTP)"))
        {
            Connect(TransportProtocol.kHttp);
        }

        GUI.enabled = (network_ != null && network_.Connected);
        if (GUI.Button(new Rect(30, 220, 240, 40), "Disconnect"))
        {
            handler_.Disconnect();

            if (!network_.SessionReliability)
                network_ = null;
        }

        if (GUI.Button(new Rect(30, 265, 240, 40), "Send a message"))
        {
            handler_.SendEchoMessage();
        }
    }

    private void Connect (TransportProtocol protocol)
    {
        FunDebug.Log("-------- Connect --------");

        FunapiTransport transport = null;
        if (network_ == null || network_.SessionReliability != with_session_reliability_)
        {
            handler_ = new TestNetwork();
            network_ = handler_.CreateNetwork(with_session_reliability_);

            FunEncoding encoding = with_protobuf_ ? FunEncoding.kProtobuf : FunEncoding.kJson;
            transport = handler_.AddTransport(protocol, kServerIp, encoding);
        }
        else
        {
            if (!network_.HasTransport(protocol))
            {
                FunEncoding encoding = with_protobuf_ ? FunEncoding.kProtobuf : FunEncoding.kJson;
                transport = handler_.AddTransport(protocol, kServerIp, encoding);
            }

            network_.SetDefaultProtocol(protocol);
        }

        if (network_ == null)
        {
            FunDebug.Log("Failed to create the network instance.");
            return;
        }

        if (transport != null)
        {
            //transport.EnablePing = true;
            //transport.SetEncryption(EncryptionType.kIFunEngine2Encryption);
        }

        network_.Start();
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";

    // member variables.
    private bool with_protobuf_ = false;
    private bool with_session_reliability_ = false;
    private TestNetwork handler_ = null;
    private FunapiNetwork network_ = null;
}
