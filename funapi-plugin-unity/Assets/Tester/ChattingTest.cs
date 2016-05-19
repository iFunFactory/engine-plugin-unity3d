// vim: tabstop=4 softtabstop=4 shiftwidth=4 expandtab
//
// Copyright (C) 2013-2016 iFunFactory Inc. All Rights Reserved.
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


public class ChattingTest : MonoBehaviour
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
        bool enable = chat_ == null;
        buttons_["create"].interactable = enable;

        enable = chat_ != null && chat_.Connected && !chat_.InChannel(kChatTestChannel);
        buttons_["join"].interactable = enable;

        enable = chat_ != null && chat_.Connected && chat_.InChannel(kChatTestChannel);
        buttons_["send"].interactable = enable;
        buttons_["leave"].interactable = enable;

        enable = chat_ != null && chat_.Connected;
        buttons_["getlist"].interactable = enable;
    }

    public void OnCreateChat ()
    {
        if (network_ == null)
            Connect();

        FunapiTransport transport = network_.GetTransport(TransportProtocol.kTcp);

        chat_ = new FunapiChatClient(network_, transport.Encoding);
        chat_.sender = "player" + UnityEngine.Random.Range(1, 100);

        chat_.ChannelListCallback += delegate(object channel_list) {
            OnMulticastChannelList(chat_.encoding, channel_list);
        };
        chat_.JoinedCallback += delegate(string channel_id, string sender) {
            FunDebug.DebugLog("JoinedCallback called. player:{0}", sender);
        };
        chat_.LeftCallback += delegate(string channel_id, string sender) {
            FunDebug.DebugLog("LeftCallback called. player:{0}", sender);
        };
        chat_.ErrorCallback += OnChatError;

        UpdateButtonState();
    }

    public void OnJoinChatChannel ()
    {
        chat_.JoinChannel(kChatTestChannel, OnChatChannelReceived);
        UpdateButtonState();
    }

    public void OnSendChatMessage ()
    {
        chat_.SendText(kChatTestChannel, "hello everyone.");
    }

    public void OnLeaveChatChannel ()
    {
        chat_.LeaveChannel(kChatTestChannel);
        UpdateButtonState();
    }

    public void OnGetChatChannelList ()
    {
        chat_.RequestChannelList();
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
        transport.StartedCallback += new TransportEventHandler(OnTransportStarted);
        network_.AttachTransport(transport);

        network_.Start();
    }

    void Disconnect ()
    {
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
        chat_ = null;
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

    void OnChatChannelReceived (string chat_channel, string sender, string text)
    {
        FunDebug.Log("Received a chat channel message.\nChannel={0}, sender={1}, text={2}",
                       chat_channel, sender, text);
    }

    void OnChatError (string channel_id, FunMulticastMessage.ErrorCode code)
    {
        if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
        {
            // If the server is closed, try to rejoin the channel.
            if (chat_ != null && chat_.Connected)
                chat_.JoinChannel(kChatTestChannel, OnChatChannelReceived);
        }

        UpdateButtonState();
    }


    // Please change this address for test.
    private const string kServerIp = "127.0.0.1";
    private const string kChatTestChannel = "chat";

    // member variables.
    private FunapiNetwork network_ = null;
    private FunapiChatClient chat_ = null;

    private Dictionary<string, Button> buttons_ = new Dictionary<string, Button>();
}
