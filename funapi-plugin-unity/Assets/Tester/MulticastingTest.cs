// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using ProtoBuf;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// protobuf
using funapi.service.multicast_message;
using plugin_messages;


public class MulticastingTest : MonoBehaviour
{
    void Awake ()
    {
        GameObject.Find("ServerIP").GetComponent<Text>().text = kServerIp;

        buttons_["create"] = GameObject.Find("ButtonCreate").GetComponent<Button>();
        buttons_["join"] = GameObject.Find("ButtonJoin").GetComponent<Button>();
        buttons_["send"] = GameObject.Find("ButtonSendMessage").GetComponent<Button>();
        buttons_["leave"] = GameObject.Find("ButtonLeave").GetComponent<Button>();
        buttons_["getlist"] = GameObject.Find("ButtonGetList").GetComponent<Button>();

        UpdateButtonState();
    }

    void UpdateButtonState ()
    {
        bool enable = multicast_ == null;
        buttons_["create"].interactable = enable;

        enable = multicast_ != null && multicast_.Connected && !multicast_.InChannel(kMulticastTestChannel);
        buttons_["join"].interactable = enable;

        enable = multicast_ != null && multicast_.Connected && multicast_.InChannel(kMulticastTestChannel);
        buttons_["send"].interactable = enable;
        buttons_["leave"].interactable = enable;

        enable = multicast_ != null && multicast_.Connected;
        buttons_["getlist"].interactable = enable;
    }

    public void OnCreateMulticast ()
    {
        if (network_ == null)
            Connect();

        FunapiTransport transport = network_.GetTransport(TransportProtocol.kTcp);

        multicast_ = new FunapiMulticastClient(network_, transport.Encoding);
        multicast_.sender = "player" + UnityEngine.Random.Range(1, 100);

        multicast_.ChannelListCallback += delegate(object channel_list) {
            OnMulticastChannelList(multicast_.encoding, channel_list);
        };
        multicast_.JoinedCallback += delegate(string channel_id, string sender) {
            FunDebug.DebugLog("JoinedCallback called. player:{0}", sender);
        };
        multicast_.LeftCallback += delegate(string channel_id, string sender) {
            FunDebug.DebugLog("LeftCallback called. player:{0}", sender);
        };
        multicast_.ErrorCallback += OnMulticastError;

        UpdateButtonState();
    }

    public void OnJoinMulticastChannel ()
    {
        multicast_.JoinChannel(kMulticastTestChannel, OnMulticastChannelReceived);
        UpdateButtonState();
    }

    public void OnSendMulticastMessage ()
    {
        if (multicast_.encoding == FunEncoding.kJson)
        {
            Dictionary<string, object> mcast_msg = new Dictionary<string, object>();
            mcast_msg["_channel"] = kMulticastTestChannel;
            mcast_msg["_bounce"] = true;
            mcast_msg["_message"] = "multicast test message";

            multicast_.SendToChannel(mcast_msg);
        }
        else
        {
            PbufHelloMessage hello_msg = new PbufHelloMessage();
            hello_msg.message = "multicast test message";

            FunMulticastMessage mcast_msg = new FunMulticastMessage();
            mcast_msg.channel = kMulticastTestChannel;
            mcast_msg.bounce = true;
            Extensible.AppendValue(mcast_msg, (int)MulticastMessageType.pbuf_hello, hello_msg);

            multicast_.SendToChannel(mcast_msg);
        }
    }

    public void OnLeaveMulticastChannel ()
    {
        multicast_.LeaveChannel(kMulticastTestChannel);
        UpdateButtonState();
    }

    public void OnGetMulticastChannelList ()
    {
        multicast_.RequestChannelList();
    }

    FunapiTransport GetNewTransport ()
    {
        FunapiTransport transport = null;
        FunEncoding encoding = FunEncoding.kJson;

        transport = new FunapiTcpTransport(kServerIp, 8012, encoding);
        transport.AutoReconnect = true;

        return transport;
    }

    void Connect ()
    {
        FunDebug.Log("-------- Connect --------");

        network_ = new FunapiNetwork(false);
        network_.StoppedAllTransportCallback += OnStoppedAllTransport;

        FunapiTransport transport = GetNewTransport();
        transport.StartedCallback += OnTransportStarted;
        network_.AttachTransport(transport);

        network_.Start();
    }

    void Disconnect ()
    {
        if (multicast_ != null)
            multicast_.LeaveAllChannels();

        if (network_.Started)
            network_.Stop();
    }

    void OnTransportStarted (TransportProtocol protocol)
    {
        UpdateButtonState();
    }

    void OnStoppedAllTransport ()
    {
        FunDebug.Log("OnStoppedAllTransport called.");
        UpdateButtonState();
        network_ = null;
        multicast_ = null;
    }

    void OnMulticastChannelList (FunEncoding encoding, object channel_list)
    {
        if (encoding == FunEncoding.kJson)
        {
            List<object> list = channel_list as List<object>;
            if (list.Count <= 0) {
                FunDebug.Log("There are no channels.");
                return;
            }

            StringBuilder data = new StringBuilder();
            foreach (Dictionary<string, object> info in list)
            {
                foreach (KeyValuePair <string, object> item in info)
                    data.AppendFormat("{0}:{1}  ", item.Key, item.Value);
                data.AppendLine();
            }
            FunDebug.Log(data.ToString());
        }
        else
        {
            List<FunMulticastChannelListMessage> list = channel_list as List<FunMulticastChannelListMessage>;
            if (list.Count <= 0) {
                FunDebug.Log("There are no channels.");
                return;
            }

            StringBuilder data = new StringBuilder();
            foreach (FunMulticastChannelListMessage info in list)
            {
                data.AppendFormat("name:{0}  members:{1}  ", info.channel_name, info.num_members);
                data.AppendLine();
            }
            FunDebug.Log(data.ToString());
        }
    }

    void OnMulticastChannelReceived (string channel_id, string sender, object body)
    {
        if (multicast_.encoding == FunEncoding.kJson)
        {
            FunDebug.Assert(body is Dictionary<string, object>);
            Dictionary<string, object> mcast_msg = body as Dictionary<string, object>;
            FunDebug.Assert (channel_id == (mcast_msg["_channel"] as string));

            string message = mcast_msg["_message"] as string;
            FunDebug.Log("Received a multicast message from the '{0}' channel.\nMessage: {1}",
                           channel_id, message);
        }
        else
        {
            FunDebug.Assert (body is FunMulticastMessage);
            FunMulticastMessage mcast_msg = body as FunMulticastMessage;
            FunDebug.Assert (channel_id == mcast_msg.channel);

            PbufHelloMessage hello_msg = Extensible.GetValue<PbufHelloMessage>(
                mcast_msg, (int)MulticastMessageType.pbuf_hello);
            if (hello_msg == null)
                return;

            FunDebug.Log("Received a multicast message from the '{0}' channel.\nMessage: {1}",
                           channel_id, hello_msg.message);
        }
    }

    void OnMulticastError (string channel_id, FunMulticastMessage.ErrorCode code)
    {
        if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
        {
            // If the server is closed, try to rejoin the channel.
            if (multicast_ != null && multicast_.Connected)
                multicast_.JoinChannel(kMulticastTestChannel, OnMulticastChannelReceived);
        }

        UpdateButtonState();
    }


    // Please change this address to your server.
    private const string kServerIp = "127.0.0.1";
    private const string kMulticastTestChannel = "multicast";

    // member variables.
    private FunapiNetwork network_ = null;
    private FunapiMulticastClient multicast_ = null;

    private Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
