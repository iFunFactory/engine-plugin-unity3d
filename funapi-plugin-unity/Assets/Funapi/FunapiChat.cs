// Copyright 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System.Collections.Generic;

// protobuf
using ProtoBuf;
using funapi.service.multicast_message;


namespace Fun
{
    public class FunapiChatClient : FunapiMulticast<string>
    {
        public FunapiChatClient (FunapiSession session, FunEncoding encoding, TransportProtocol protocol = TransportProtocol.kDefault)
            : base(session, encoding, protocol)
        {
        }

        public bool SendText (string channel_id, string text)
        {
            if (!Connected)
                return false;

            if (encoding_ == FunEncoding.kJson)
            {
                Dictionary<string, object> mcast = new Dictionary<string, object>();
                mcast[kChannelId] = channel_id;
                mcast[kBounce] = true;
                mcast[kMessage] = text;

                SendToChannel(mcast);
            }
            else
            {
                FunChatMessage chat = new FunChatMessage ();
                chat.text = text;

                FunMulticastMessage mcast = FunapiMessage.CreateMulticastMessage(chat, MulticastMessageType.chat);
                mcast.channel = channel_id;
                mcast.bounce = true;

                SendToChannel(mcast);
            }

            return true;
        }

        protected override void onMessageCallback (string channel_id, string user_id, object message)
        {
            if (encoding_ == FunEncoding.kJson)
            {
                string text = json_helper_.GetStringField(message, kMessage);

                base.onMessageCallback(channel_id, user_id, text);
            }
            else
            {
                FunMulticastMessage mcast = message as FunMulticastMessage;
                FunChatMessage chat = FunapiMessage.GetMulticastMessage<FunChatMessage>(mcast, MulticastMessageType.chat);
                if (chat == null)
                    return;

                base.onMessageCallback(channel_id, user_id, chat.text);
            }
        }


        const string kChannelId = "_channel";
        const string kBounce = "_bounce";
        const string kMessage = "_message";
    }
}
