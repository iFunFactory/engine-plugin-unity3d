// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine.TestTools;
using UnityEngine;

// protobuf
using funapi.distribution.fun_dedicated_server_rpc_message;
using plugin_dedicated_server_rpc_messages;


public class TestDSRpc
{
    [UnityTest]
    public IEnumerator Test ()
    {
        yield return new TestImpl();
    }

    class TestImpl : TestBase
    {
        public TestImpl ()
        {
            DSRpcOption option = new DSRpcOption();
            option.Tag = "Test";
            option.AddHost("127.0.0.1", 8016);
            option.AddHost("127.0.0.1", 8017);
            option.AddHost("127.0.0.1", 8018);

            FunapiDedicatedServerRpc rpc = new FunapiDedicatedServerRpc(option);
            rpc.SetHandler("echo", onEcho);
            rpc.SetHandler("nav", onNavRequest);

            rpc.Start();

            setTestTimeoutWithoutFail(30f);
        }

        FunDedicatedServerRpcMessage onEcho (string type, FunDedicatedServerRpcMessage request)
        {
            EchoDedicatedServerRpcMessage echo = new EchoDedicatedServerRpcMessage();
            echo.message = "echo from client";

            return FunapiDSRpcMessage.CreateMessage(echo, MessageType.echo_ds_rpc);;
        }

        FunDedicatedServerRpcMessage onNavRequest (string type, FunDedicatedServerRpcMessage request)
        {
            NavRequest req = FunapiDSRpcMessage.GetMessage<NavRequest>(request, MessageType.nav_request);
            // NavMeshAgent.CalculatePath(req.destination, path);

            NavReply reply = new NavReply();
            // For the test
            Vector3[] corners = new [] { Vector3.zero, Vector3.one, Vector3.back };
            for (int i = 0; i < corners.Length; ++i)
            {
                NavVector3 point = new NavVector3();
                point.x = corners[i].x;
                point.y = corners[i].y;
                point.z = corners[i].z;
                reply.waypoints.Add(point);
            }

            return FunapiDSRpcMessage.CreateMessage(reply, MessageType.nav_reply);
        }
    }
}
