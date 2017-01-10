// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using Fun;
using System.Collections;
using UnityEngine;

// protobuf
using funapi.service.multicast_message;


public partial class Tester
{
    public class Chatting : Base
    {
        public override IEnumerator Start (FunapiSession session, UIOption option)
        {
            // MulticastClient
            FunEncoding encoding = option.tcpEncoding;
            chat_ = new FunapiChatClient(session, encoding);
            chat_.sender = "player_" + UnityEngine.Random.Range(1, 100);

            chat_.ChannelListCallback += delegate (object channel_list) {
                onMulticastChannelList(encoding, channel_list);
            };
            chat_.JoinedCallback += delegate (string channel_id, string sender) {
                FunDebug.DebugLog("JoinedCallback called. player:{0}", sender);
            };
            chat_.LeftCallback += delegate (string channel_id, string sender) {
                FunDebug.DebugLog("LeftCallback called. player:{0}", sender);
            };
            chat_.ErrorCallback += onMulticastError;

            // Getting channel list
            chat_.RequestChannelList();
            yield return new WaitForSeconds(0.1f);

            // Join the channel
            chat_.JoinChannel(kChannelName, onChatReceived);
            yield return new WaitForSeconds(0.1f);

            // Send messages
            for (int i = 0; i < sendingCount; ++i)
            {
                chat_.SendText(kChannelName, "hello everyone.");
                yield return new WaitForSeconds(0.1f);
            }

            // Getting channel list
            chat_.RequestChannelList();
            yield return new WaitForSeconds(0.1f);

            // Leave the channel
            chat_.LeaveChannel(kChannelName);
            yield return new WaitForSeconds(0.2f);

            chat_.Clear();
            chat_ = null;

            OnFinished();
        }

        void onChatReceived (string chat_channel, string sender, string text)
        {
            FunDebug.Log("Received a chat channel message.\nChannel={0}, sender={1}, text={2}",
                         chat_channel, sender, text);
        }

        void onMulticastError (string channel_id, FunMulticastMessage.ErrorCode code)
        {
            if (code == FunMulticastMessage.ErrorCode.EC_CLOSED)
            {
                // If the server is closed, try to rejoin the channel.
                if (chat_ != null && chat_.Connected)
                    chat_.JoinChannel(kChannelName, onChatReceived);
            }
        }


        const string kChannelName = "chatting";

        // Member variables.
        FunapiChatClient chat_;
    }
}
