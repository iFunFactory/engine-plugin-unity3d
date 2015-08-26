// Copyright (C) 2013-2015 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;


namespace Fun
{
    // Download file-related.
    public class DownloadFileInfo
    {
        public string path;         // save file path
        public uint size;           // file size
        public string hash;         // md5 hash
        public string hash_front;   // front part of md5 hash
    }

    public enum DownloadResult
    {
        SUCCESS,
        FAILED
    }


    public class FunapiHttpDownloader
    {
        public FunapiHttpDownloader (bool enable_verify = false)
        {
            manager_ = FunapiManager.instance;
            enable_verify_ = enable_verify;

            // List file handler
            web_client_.DownloadDataCompleted += new DownloadDataCompletedEventHandler(DownloadDataCompleteCb);

            // Download file handler
            web_client_.DownloadProgressChanged += new DownloadProgressChangedEventHandler(DownloadProgressChangedCb);
            web_client_.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCompleteCb);
        }

        // Start downloading
        public void GetDownloadList (string hostname_or_ip, UInt16 port, bool https, string target_path)
        {
            string url = String.Format("{0}://{1}:{2}",
                                       (https ? "https" : "http"), hostname_or_ip, port);

            GetDownloadList(url, target_path);
        }

        public void GetDownloadList (string url, string target_path)
        {
            if (ReadyCallback == null)
            {
                Debug.LogError("You must register the ReadyCallback first.");
                return;
            }

            mutex_.WaitOne();

            try
            {
                if (IsDownloading)
                {
                    Debug.LogWarning(String.Format("The resource file is being downloaded. (Url: {0})\n"
                                                   + "Please try again after the download is completed.", url));
                    return;
                }

                state_ = State.Start;

                string host_url = url;
                if (host_url[host_url.Length - 1] != '/')
                    host_url += "/";
                host_url_ = host_url;

                target_path_ = target_path;
                if (target_path_[target_path_.Length - 1] != '/')
                    target_path_ += "/";
                target_path_ += kRootPath + "/";
                Debug.Log(String.Format("Download path:{0}", target_path_));

                cur_download_count_ = 0;
                cur_download_size_ = 0;
                total_download_count_ = 0;
                total_download_size_ = 0;

                // Gets list file
                DownloadListFile(host_url_);
            }
            finally
            {
                mutex_.ReleaseMutex();
            }
        }

        public void StartDownload()
        {
            if (state_ != State.Ready)
            {
                Debug.LogError("You must call GetDownloadList function first.");
                return;
            }

            mutex_.WaitOne();

            if (total_download_count_ > 0)
            {
                Debug.Log(String.Format("Ready to download {0} files ({1:F2}MB)",
                                        total_download_count_,
                                        (float)total_download_size_ / (1024f * 1024f)));
            }

            state_ = State.Downloading;
            check_time_ = DateTime.Now;

            // Deletes files
            DeleteLocalFiles();

            // Starts download
            DownloadResourceFile();

            mutex_.ReleaseMutex();
        }

        public void Stop()
        {
            mutex_.WaitOne();

            if (IsDownloading)
            {
                web_client_.CancelAsync();
                OnFinishedCallback(DownloadResult.FAILED);
            }

            mutex_.ReleaseMutex();
        }

        public bool IsDownloading
        {
            get { return state_ == State.Start || state_ == State.Ready || state_ == State.Downloading; }
        }

        public int CurrentDownloadFileCount
        {
            get { return cur_download_count_; }
        }

        public int TotalDownloadFileCount
        {
            get { return total_download_count_; }
        }

        public UInt64 CurDownloadFileSize
        {
            get {  return cur_download_size_; }
        }

        public UInt64 TotalDownloadFileSize
        {
            get {  return total_download_size_; }
        }

        // Checks download file list
        private IEnumerator CheckFileList (List<DownloadFileInfo> list)
        {
            List<string> verify_file_list = new List<string>();
            check_time_ = DateTime.Now;

            if (Directory.Exists(target_path_))
            {
                string[] files = Directory.GetFiles(target_path_, "*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Debug.Log("Checks local files...");

                    foreach (string s in files)
                    {
                        string path = s.Replace('\\', '/');
                        string find_path = path.Substring(target_path_.Length);
                        DownloadFileInfo file = list.Find(i => i.path == find_path);
                        if (file != null)
                        {
                            FileInfo info = new FileInfo(path);
                            if (file.size != info.Length)
                            {
                                remove_list_.Add(path);
                            }
                            else if (enable_verify_)
                            {
                                verify_file_list.Add(file.path);

                                while (verify_file_list.Count > kMaxAsyncRequestCount) {
                                    yield return new WaitForEndOfFrame();
                                }

                                MD5Async.Compute(ref path, ref file, delegate (string p, DownloadFileInfo f, bool is_match)
                                {
                                    if (VerifyCallback != null)
                                        VerifyCallback(path);

                                    verify_file_list.Remove(f.path);

                                    if (is_match)
                                        list.Remove(f);
                                    else
                                        remove_list_.Add(p);
                                });
                            }
                            else
                            {
                                list.Remove(file);
                            }
                        }
                        else
                        {
                            remove_list_.Add(path);
                        }

                        yield return new WaitForEndOfFrame();
                    }
                }
            } // if (Directory.Exists(target_path_))

            while (verify_file_list.Count > 0)
            {
                yield return new WaitForSeconds(0.1f);
            }

            TimeSpan span = new TimeSpan(DateTime.Now.Ticks - check_time_.Ticks);
            Debug.Log(string.Format("File check total time - {0:F2}s", span.TotalMilliseconds / 1000f));

            total_download_count_ = download_list_.Count;

            foreach (DownloadFileInfo item in list)
            {
                total_download_size_ += item.size;
            }

            if (total_download_count_ > 0)
            {
                state_ = State.Ready;
                if (ReadyCallback != null)
                    ReadyCallback(total_download_count_, total_download_size_);
            }
            else
            {
                DeleteLocalFiles();

                state_ = State.Completed;
                Debug.Log("All resources are up to date.");
                OnFinishedCallback(DownloadResult.SUCCESS);
            }
        }

        private void DownloadListFile (string url)
        {
            bool failed = false;
            try
            {
                // Request a list of download files.
                Debug.Log("Getting list file from " + url);
                web_client_.DownloadDataAsync(new Uri(url));
            }
            catch (Exception e)
            {
                Debug.Log("Failure in DownloadListFile: " + e.ToString());
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void DeleteLocalFiles ()
        {
            if (remove_list_.Count <= 0)
                return;

            foreach (string path in remove_list_)
            {
                File.Delete(path);
                Debug.Log("Deleted resource file \npath: " + path);
            }

            remove_list_.Clear();
        }

        // Downloading files.
        private void DownloadResourceFile ()
        {
            if (download_list_.Count <= 0)
            {
                TimeSpan span = new TimeSpan(DateTime.Now.Ticks - check_time_.Ticks);
                Debug.Log(string.Format("File download total time - {0:F2}s", span.TotalMilliseconds / 1000f));

                state_ = State.Completed;
                Debug.Log("Download completed.");
                OnFinishedCallback(DownloadResult.SUCCESS);
            }
            else
            {
                DownloadFileInfo info = download_list_[0];

                // Check directory
                string path = target_path_;
                int offset = info.path.LastIndexOf('/');
                if (offset >= 0)
                    path += info.path.Substring(0, offset);

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                string file_path = target_path_ + info.path;
                if (File.Exists(file_path))
                    File.Delete(file_path);

                // Requests a file.
                Debug.Log("Download file - " + file_path);
                web_client_.DownloadFileAsync(new Uri(host_url_ + info.path), file_path, info);
            }
        }

        // Callback function for list of files
        private void DownloadDataCompleteCb (object sender, DownloadDataCompletedEventArgs ar)
        {
            mutex_.WaitOne();

            bool failed = false;
            try
            {
                if (ar.Error != null)
                {
                    Debug.Log("Exception Error: " + ar.Error);
                    DebugUtils.Assert(false);
                    failed = true;
                }
                else
                {
                    // It can be true when CancelAsync() called in Stop().
                    if (ar.Cancelled)
                        return;

                    // Parse json
                    string data = Encoding.UTF8.GetString(ar.Result);
                    Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;

                    //Debug.Log("Json data >>  " + data);

                    // Redirect url
                    if (json.ContainsKey("url"))
                    {
                        string url = json["url"] as string;
                        if (url[url.Length - 1] != '/')
                            url += "/";

                        host_url_ = url;
                        Debug.Log("Download url : " + host_url_);
                    }

                    List<object> list = json["data"] as List<object>;
                    if (list.Count <= 0)
                    {
                        Debug.Log("Invalid list data. List count is 0.");
                        DebugUtils.Assert(false);
                        failed = true;
                    }
                    else
                    {
                        download_list_.Clear();

                        foreach (Dictionary<string, object> node in list)
                        {
                            DownloadFileInfo info = new DownloadFileInfo();
                            info.path = node["path"] as string;
                            info.size = Convert.ToUInt32(node["size"]);
                            info.hash = node["md5"] as string;
                            if (node.ContainsKey("md5_front"))
                                info.hash_front = node["md5_front"] as String;
                            else
                                info.hash_front = "";

                            download_list_.Add(info);
                        }

                        // Checks files
                        manager_.AddEvent(() =>
                            manager_.StartCoroutine(CheckFileList(download_list_)));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in DownloadDataCompleteCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        // Callback function for download progress.
        private void DownloadProgressChangedCb (object sender, DownloadProgressChangedEventArgs ar)
        {
            if (UpdateCallback == null)
                return;

            var info = (DownloadFileInfo)ar.UserState;
            if (info != null)
            {
                UpdateCallback(info.path, ar.BytesReceived, ar.TotalBytesToReceive, ar.ProgressPercentage);
            }
        }

        // Callback function for downloaded file.
        private void DownloadFileCompleteCb (object sender, System.ComponentModel.AsyncCompletedEventArgs ar)
        {
            mutex_.WaitOne();

            bool failed = false;
            try
            {
                // It can be true when CancelAsync() called in Stop().
                if (ar.Cancelled)
                    return;

                if (ar.Error != null)
                {
                    Debug.Log("Exception Error: " + ar.Error);
                    DebugUtils.Assert(false);
                    failed = true;
                }
                else
                {
                    var info = (DownloadFileInfo)ar.UserState;
                    if (info == null)
                    {
                        Debug.Log("DownloadFileInfo object is null.");
                        failed = true;
                    }
                    else
                    {
                        ++cur_download_count_;
                        cur_download_size_ += info.size;
                        download_list_.Remove(info);

                        DownloadResourceFile();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in DownloadFileCompleteCb: " + e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                Stop();
            }
        }

        private void OnFinishedCallback (DownloadResult code)
        {
            if (FinishedCallback != null)
                FinishedCallback(code);
        }


        enum State
        {
            None,
            Ready,
            Start,
            Downloading,
            Completed
        }

        public delegate void VerifyEventHandler (string path);
        public delegate void ReadyEventHandler (int total_count, UInt64 total_size);
        public delegate void UpdateEventHandler (string path, long bytes_received, long total_bytes, int percentage);
        public delegate void FinishEventHandler (DownloadResult code);

        public event VerifyEventHandler VerifyCallback;
        public event ReadyEventHandler ReadyCallback;
        public event UpdateEventHandler UpdateCallback;
        public event FinishEventHandler FinishedCallback;

        // Save file-related constants.
        private static readonly string kRootPath = "client_data";
        private static readonly int kMaxAsyncRequestCount = 10;

        // member variables.
        private State state_ = State.None;
        private bool enable_verify_ = true;
        private string host_url_ = "";
        private string target_path_ = "";
        private int cur_download_count_ = 0;
        private int total_download_count_ = 0;
        private UInt64 cur_download_size_ = 0;
        private UInt64 total_download_size_ = 0;

        private FunapiManager manager_ = null;
        private Mutex mutex_ = new Mutex();
        private DateTime check_time_;
        private WebClient web_client_ = new WebClient();
        private List<DownloadFileInfo> download_list_ = new List<DownloadFileInfo>();
        private List<string> remove_list_ = new List<string>();
    }
}
