// Copyright 2018 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.TestTools;

// protobuf
using funapi.network.fun_message;
using plugin_messages;


public class TestCompression
{
    [UnityTest]
    public IEnumerator Deflate_Json ()
    {
        yield return new TestImpl (FunEncoding.kJson, FunCompressionType.kDeflate);
    }

    [UnityTest]
    public IEnumerator Zstd_Protobuf ()
    {
        yield return new TestImpl (FunEncoding.kProtobuf, FunCompressionType.kZstd);
    }


    class TestImpl : TestSessionBase
    {
        public TestImpl (FunEncoding encoding, FunCompressionType comp_type, bool use_dic = false)
        {
            session = FunapiSession.Create(TestInfo.ServerIp);

            session.CreateCompressorCallback += delegate (TransportProtocol protocol)
            {
                if (comp_type == FunCompressionType.kZstd && use_dic)
                {
                    var compressor = new FunapiZstdCompressor();
                    compressor.compression_threshold = 128;

                    TextAsset text = Resources.Load<TextAsset>("zstd_dict");
                    if (text != null)
                    {
                        byte[] zstd_dic = Convert.FromBase64String(text.text);
                        compressor.Create(zstd_dic);
                    }
                    else
                    {
                        FunDebug.LogWarning("Couldn't find a dictionary file in your resources folder.");
                    }

                    compressor_ = compressor;
                    return compressor;
                }

                return null;
            };

            session.SessionEventCallback += delegate (SessionEventType type, string sessionid)
            {
                if (type == SessionEventType.kConnected)
                {
                    sendEchoMessageWithCount(TransportProtocol.kTcp, 5);
                    sendEchoMessageWithCount(TransportProtocol.kUdp, 5);
                    sendEchoMessageWithCount(TransportProtocol.kHttp, 5);
                }
            };

            session.ReceivedMessageCallback += delegate (string type, object message)
            {
                onReceivedEchoMessage(type, message);

                if (isReceivedAllMessages)
                    onTestFinished();
            };

            setEchoMessage(dummyText);
            setTestTimeout(3f);

            startConnect(TransportProtocol.kTcp, encoding, comp_type);
            startConnect(TransportProtocol.kUdp, encoding, comp_type);
            startConnect(TransportProtocol.kHttp, encoding, comp_type);
        }

        void startConnect (TransportProtocol protocol, FunEncoding encoding, FunCompressionType type)
        {
            TransportOption option = newTransportOption(protocol);
            option.CompressionType = type;

            ushort port = getPort("compression", protocol, encoding);
            session.Connect(protocol, encoding, port, option);
        }

        protected override void onTestFinished ()
        {
            if (compressor_ != null)
                compressor_.Destroy();

            base.onTestFinished();
        }


        static readonly string dummyText = "{\"id\":12032,\"pos_x\":31.01,\"pos_z\":45.5293984741," +
                                           "\"dir_x\":-14.199799809265137,\"dir_z\":11.899918530274," +
                                           "\"look_x\":1.100000381469727,\"look_z\":11.600100381469727," +
                                           "\"_msgtype\":\"request_move\",\"_nick\":\"snooooooow\"}";

        FunapiZstdCompressor compressor_ = null;
    }
}
