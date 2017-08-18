using Fun;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// protobuf
using plugin_messages;
using funapi.network.fun_message;


namespace test_winform
{
    class Client
    {
        public Client(int id, string server_ip)
        {
            id_ = id;
            server_ip_ = server_ip;
        }

        public void Connect(SessionOption session_option)
        {
            message_number_ = 0;

            if (session_ == null)
            {
                session_ = FunapiSession.Create(server_ip_, session_option);
                session_.SessionEventCallback += onSessionEvent;
                session_.TransportEventCallback += onTransportEvent;
                session_.TransportErrorCallback += onTransportError;
                session_.ReceivedMessageCallback += onReceivedMessage;

                for (int i = 0; i < protocols.Count; ++i)
                {
                    TransportOption option = new TransportOption();
                    if (protocols[i] == TransportProtocol.kTcp)
                    {
                        TcpTransportOption tcp_option = new TcpTransportOption();
                        //tcp_option.EnablePing = true;
                        tcp_option.PingIntervalSeconds = 3;
                        tcp_option.PingTimeoutSeconds = 20;
                        option = tcp_option;
                    }
                    else if (protocols[i] == TransportProtocol.kHttp)
                    {
                        option = new HttpTransportOption();
                    }
                    else
                    {
                        option = new TransportOption();
                    }

                    option.ConnectionTimeout = 5f;

                    //if (protocols[i] == TransportProtocol.kTcp)
                    //    option.Encryption = EncryptionType.kIFunEngine1Encryption;
                    //else
                    //    option.Encryption = EncryptionType.kIFunEngine2Encryption;

                    int enc = (int)protocols[i] - 1;
                    ushort port = getPort(protocols[i], encodings[enc]);
                    session_.Connect(protocols[i], encodings[enc], port, option);
                }
            }
            else
            {
                for (int i = 0; i < protocols.Count; ++i)
                {
                    session_.Connect(protocols[i]);
                }
            }
        }

        public void Stop()
        {
            if (session_ != null && session_.Connected)
                session_.Stop();
        }

        public bool Connected
        {
            get { return session_ != null && session_.Connected; }
        }

        public void Update()
        {
            if (session_ != null)
                session_.updateFrame();
        }

        public void SendMessage()
        {
            for (int i = 0; i < protocols.Count; ++i)
            {
                if (protocols[i] == TransportProtocol.kTcp)
                    SendMessage(TransportProtocol.kTcp, "tcp message");
                else if (protocols[i] == TransportProtocol.kUdp)
                    SendMessage(TransportProtocol.kUdp, "udp message");
                else if (protocols[i] == TransportProtocol.kHttp)
                    SendMessage(TransportProtocol.kHttp, "http message");
            }
        }

        public void SendMessage(TransportProtocol protocol, string message)
        {
            FunEncoding encoding = encodings[(int)protocol - 1];
            if (encoding == FunEncoding.kJson)
            {
                Dictionary<string, object> echo = new Dictionary<string, object>();
                echo["message"] = message;
                session_.SendMessage("echo", echo, protocol);
            }
            else
            {
                PbufEchoMessage echo = new PbufEchoMessage();
                echo.msg = message;
                FunMessage fmsg = FunapiMessage.CreateFunMessage(echo, MessageType.pbuf_echo);
                session_.SendMessage("pbuf_echo", fmsg, protocol);
            }
        }


        ushort getPort(TransportProtocol protocol, FunEncoding encoding)
        {
            ushort port = 0;
            if (protocol == TransportProtocol.kTcp)
                port = (ushort)(encoding == FunEncoding.kJson ? 8012 : 8022);
            else if (protocol == TransportProtocol.kUdp)
                port = (ushort)(encoding == FunEncoding.kJson ? 8013 : 8023);
            else if (protocol == TransportProtocol.kHttp)
                port = (ushort)(encoding == FunEncoding.kJson ? 8018 : 8028);
            port += 200;

            return port;
        }

        void onSessionEvent(SessionEventType type, string session_id)
        {
        }

        void onTransportEvent(TransportProtocol protocol, TransportEventType type)
        {
            if (type == TransportEventType.kConnectionFailed ||
                type == TransportEventType.kConnectionTimedOut ||
                type == TransportEventType.kDisconnected)
            {
                session_.Stop(protocol);
            }
        }

        void onTransportError(TransportProtocol protocol, TransportError error)
        {
        }

        void onReceivedMessage(string type, object message)
        {
            string echo_msg = "";

            if (type == "echo")
            {
                Dictionary<string, object> json = message as Dictionary<string, object>;
                echo_msg = json["message"] as string;
                FunDebug.Log("[{0}:{2}] {1}", id_, echo_msg, ++message_number_);
            }
            else if (type == "pbuf_echo")
            {
                FunMessage msg = message as FunMessage;
                object obj = FunapiMessage.GetMessage(msg, MessageType.pbuf_echo);
                if (obj == null)
                    return;

                PbufEchoMessage echo = obj as PbufEchoMessage;
                echo_msg = echo.msg;
                FunDebug.Log("[{0}:{2}] {1}", id_, echo_msg, ++message_number_);
            }

            if (message_number_ < 9)
            {
                if (echo_msg.StartsWith("tcp"))
                    SendMessage(TransportProtocol.kTcp, echo_msg);
                else if (echo_msg.StartsWith("udp"))
                    SendMessage(TransportProtocol.kUdp, echo_msg);
                else if (echo_msg.StartsWith("http"))
                    SendMessage(TransportProtocol.kHttp, echo_msg);
            }
            else
            {
                Stop();
            }
        }


        // Protocol constants.
        static readonly List<TransportProtocol> protocols = new List<TransportProtocol>() {
            TransportProtocol.kTcp, TransportProtocol.kHttp };

        // Encoding constants.
        static readonly List<FunEncoding> encodings = new List<FunEncoding>() {
            FunEncoding.kJson, FunEncoding.kJson, FunEncoding.kJson };


        // Member variables.
        int id_ = -1;
        string server_ip_;
        int message_number_ = 0;

        FunapiSession session_ = null;
    }
}
