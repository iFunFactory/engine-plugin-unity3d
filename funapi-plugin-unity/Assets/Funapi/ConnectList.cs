// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Net;
#if !NO_UNITY
using UnityEngine;
#endif


namespace Fun
{
    public class HostAddr
    {
        public HostAddr (string host, UInt16 port)
        {
            this.host = host;
            this.port = port;
        }

        public string host;
        public UInt16 port;
    }

    public class HostHttp : HostAddr
    {
        public HostHttp (string host, UInt16 port, bool https = false)
            : base(host, port)
        {
            this.https = https;
        }

        public bool https;
    }

    internal class HostIP : HostAddr
    {
        public HostIP (string host, IPAddress ip, UInt16 port)
            : base(host, port)
        {
            this.ip = ip;
        }

        public IPAddress ip;
    }


    internal class ConnectList
    {
        internal void Add (string hostname, UInt16 port)
        {
            IPAddress[] list = Dns.GetHostAddresses(hostname);
            if (list == null) {
                DebugUtils.Log("ConnectList - Can't find any ip address with hostname [{0}].", hostname);
                return;
            }

            foreach (IPAddress ip in list)
            {
                addr_list_.Add(new HostIP(hostname, ip, port));
            }

            DebugUtils.Log("[{0}] Dns address count : {1}", hostname, addr_list_.Count);
        }

        internal void Add (string hostname, UInt16 port, bool https)
        {
            IPAddress[] list = Dns.GetHostAddresses(hostname);
            if (list == null) {
                DebugUtils.Log("ConnectList - Can't find any ip address with hostname [{0}].", hostname);
                return;
            }

            foreach (IPAddress ip in list)
            {
                addr_list_.Add(new HostHttp(ip.ToString(), port, https));
            }

            DebugUtils.Log("[{0}] Dns address count : {1}", hostname, addr_list_.Count);
        }

        internal void Add (List<HostAddr> list)
        {
            if (list == null || list.Count <= 0) {
                DebugUtils.Log("ConnectList - Invalid connect list parameter.");
                return;
            }

            addr_list_.AddRange(list);
        }

        internal void Add (HostAddr addr)
        {
            addr_list_.Add(addr);
        }

        internal void Clear ()
        {
            addr_list_.Clear();
            addr_list_index_ = 0;
            first_ = true;
        }

        internal void SetFirst ()
        {
            addr_list_index_ = 0;
            first_ = true;
        }

        internal void SetLast ()
        {
            addr_list_index_ = addr_list_.Count;
        }

        internal HostAddr GetCurAddress ()
        {
            if (!IsCurAvailable)
                return null;

            return addr_list_[addr_list_index_];
        }

        internal HostAddr GetNextAddress ()
        {
            if (first_)
            {
                first_ = false;
                return GetCurAddress();
            }

            if (!IsNextAvailable)
                return null;

            ++addr_list_index_;
            return addr_list_[addr_list_index_];
        }

        internal bool IsCurAvailable
        {
            get { return addr_list_.Count > 0 && addr_list_index_ < addr_list_.Count; }
        }

        internal bool IsNextAvailable
        {
            get { return addr_list_.Count > 0 && addr_list_index_ + 1 < addr_list_.Count; }
        }


        // member variables.
        private List<HostAddr> addr_list_ = new List<HostAddr>();
        private int addr_list_index_ = 0;
        private bool first_ = true;
    }

}  // namespace Fun
