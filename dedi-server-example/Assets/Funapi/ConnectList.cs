// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using System;
using System.Collections.Generic;
using System.Net;


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

    public class HostIP : HostAddr
    {
        public HostIP (string host, IPAddress ip, UInt16 port)
            : base(host, port)
        {
            this.ip = ip;
        }

        public IPAddress ip;
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


    public class ConnectList
    {
        public void Add (string hostname, UInt16 port)
        {
            IPAddress[] list = Dns.GetHostAddresses(hostname);
            if (list == null) {
                FunDebug.LogWarning("ConnectList.Add - Can't find any ip address with '{0}' host.", hostname);
                return;
            }

            FunDebug.DebugLog1("[{0}] Dns address count is {1}.", hostname, list.Length);

            foreach (IPAddress ip in list)
            {
                addr_list_.Add(new HostIP(hostname, ip, port));
                FunDebug.DebugLog1("  > {0} ({1})", ip, ip.AddressFamily);
            }
        }

        public void Add (string hostname, UInt16 port, bool https)
        {
            addr_list_.Add(new HostHttp(hostname, port, https));
        }

        public void Add (List<HostAddr> list)
        {
            if (list == null || list.Count <= 0) {
                FunDebug.LogWarning("ConnectList.Add - You must pass a list of HostAddr as a parameter.");
                return;
            }

            addr_list_.AddRange(list);
        }

        public void Replace (string hostname, ushort port)
        {
            if (addr_list_.Count <= 0)
                return;

            HostAddr addr = addr_list_[0];
            Clear();

            if (addr is HostHttp)
            {
                HostHttp http = (HostHttp)addr;
                Add(hostname, port, http.https);
            }
            else
            {
                Add(hostname, port);
            }
        }

        public void Clear ()
        {
            addr_list_.Clear();
            addr_list_index_ = 0;
            first_ = true;
        }

        public void SetFirst ()
        {
            addr_list_index_ = 0;
            first_ = true;
        }

        public void SetLast ()
        {
            addr_list_index_ = addr_list_.Count;
        }

        public HostAddr GetCurAddress ()
        {
            if (!IsCurAvailable)
                return null;

            return addr_list_[addr_list_index_];
        }

        public HostAddr GetNextAddress ()
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

        public bool IsCurAvailable
        {
            get { return addr_list_.Count > 0 && addr_list_index_ < addr_list_.Count; }
        }

        public bool IsNextAvailable
        {
            get { return addr_list_.Count > 0 && addr_list_index_ + 1 < addr_list_.Count; }
        }


        // member variables.
        List<HostAddr> addr_list_ = new List<HostAddr>();
        int addr_list_index_ = 0;
        bool first_ = true;
    }

}  // namespace Fun
