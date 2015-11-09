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
        public string hash_front;   // front part of file (1MB)
    }

    public enum DownloadResult
    {
        SUCCESS,
        FAILED
    }


    public class FunapiHttpDownloader
    {
        public FunapiHttpDownloader ()
        {
            manager_ = FunapiManager.instance;

            // List file handler
            web_client_.DownloadDataCompleted += new DownloadDataCompletedEventHandler(DownloadDataCompleteCb);

            // Download file handler
            web_client_.DownloadProgressChanged += new DownloadProgressChangedEventHandler(DownloadProgressChangedCb);
            web_client_.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCompleteCb);
        }

        // Start downloading
        public void GetDownloadList (string hostname_or_ip, UInt16 port, bool https, string target_path)
        {
            string url = string.Format("{0}://{1}:{2}",
                                       (https ? "https" : "http"), hostname_or_ip, port);

            GetDownloadList(url, target_path);
        }

        public void GetDownloadList (string url, string target_path)
        {
            if (ReadyCallback == null)
            {
                DebugUtils.LogError("You must register the ReadyCallback first.");
                return;
            }

            mutex_.WaitOne();

            try
            {
                if (IsDownloading)
                {
                    DebugUtils.LogWarning("The resource file is being downloaded. (Url: {0})\n"
                                          + "Please try again after the download is completed.", url);
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
                DebugUtils.Log("Download path:{0}", target_path_);

                cur_download_count_ = 0;
                cur_download_size_ = 0;
                total_download_count_ = 0;
                total_download_size_ = 0;

                LoadCachedList();

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
                DebugUtils.LogError("You must call GetDownloadList function first.");
                return;
            }

            mutex_.WaitOne();

            if (total_download_count_ > 0)
            {
                DebugUtils.Log("Ready to download {0} files ({1:F2}MB)",
                               total_download_count_,
                               (float)total_download_size_ / (1024f * 1024f));
            }

            state_ = State.Downloading;
            check_time_ = DateTime.Now;
            retry_download_count_ = 0;

            // Deletes files
            DeleteLocalFiles();

            // Starts download
            DownloadResourceFile();

            mutex_.ReleaseMutex();
        }

        public void ContinueDownload()
        {
            if (download_list_.Count > 0)
            {
                DownloadResourceFile();
            }
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

        private void LoadCachedList ()
        {
            cached_list_.Clear();

            string path = target_path_ + kCachedFileName;
            if (!File.Exists(path))
                return;

            StreamReader stream = File.OpenText(path);
            string data = stream.ReadToEnd();
            stream.Close();

            if (data.Length <= 0)
            {
                DebugUtils.Log("Failed to get a cached file list.");
                DebugUtils.Assert(false);
                return;
            }

            Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;
            List<object> list = json["list"] as List<object>;

            foreach (Dictionary<string, object> node in list)
            {
                DownloadFileInfo info = new DownloadFileInfo();
                info.path = node["path"] as string;
                info.size = Convert.ToUInt32(node["size"]);
                info.hash = node["hash"] as string;
                if (node.ContainsKey("front"))
                    info.hash_front = node["front"] as string;
                else
                    info.hash_front = "";

                cached_list_.Add(info);
            }
        }

        private void UpdateCachedList ()
        {
            StringBuilder data = new StringBuilder();
            data.Append("{ \"list\": [ ");

            int index = 0;
            foreach (DownloadFileInfo info in cached_list_)
            {
                data.AppendFormat("{{ \"path\":\"{0}\", ", info.path);
                data.AppendFormat("\"size\":{0}, ", info.size);
                if (info.hash_front.Length > 0)
                    data.AppendFormat("\"front\":\"{0}\", ", info.hash_front);
                data.AppendFormat("\"hash\":\"{0}\" }}", info.hash);

                if (++index < cached_list_.Count)
                    data.Append(", ");
            }

            data.Append(" ] }");

            string path = target_path_ + kCachedFileName;
            FileStream file = File.Open(path, FileMode.Create);
            StreamWriter stream = new StreamWriter(file);
            stream.Write(data.ToString());
            stream.Flush();
            stream.Close();
        }

        // Checks download file list
        private IEnumerator CheckFileList (List<DownloadFileInfo> list)
        {
            List<DownloadFileInfo> tmp_list = new List<DownloadFileInfo>(list);
            List<string> verify_file_list = new List<string>();
            List<string> remove_list = new List<string>();
            Queue<int> rnd_list = new Queue<int>();
            bool verify_success = true;
            int rnd_index = -1;

            DateTime cached_time = File.GetLastWriteTime(target_path_ + kCachedFileName);
            check_time_ = DateTime.Now;

            delete_file_list_.Clear();

            // Randomly check list
            if (cached_list_.Count > 0)
            {
                int max_count = cached_list_.Count;
                int count = Math.Min(Math.Max(1, max_count / 10), 10);
                System.Random rnd = new System.Random((int)DateTime.Now.Ticks);

                while (rnd_list.Count < count)
                {
                    rnd_index = rnd.Next(1, max_count + 1) - 1;
                    if (!rnd_list.Contains(rnd_index))
                        rnd_list.Enqueue(rnd_index);
                }
                DebugUtils.DebugLog("Random check file count is {0}", rnd_list.Count);

                rnd_index = rnd_list.Count > 0 ? rnd_list.Dequeue() : -1;
            }

            // Checks local files
            int index = 0;
            foreach (DownloadFileInfo file in cached_list_)
            {
                DownloadFileInfo item = list.Find(i => i.path == file.path);
                if (item != null)
                {
                    string path = target_path_ + file.path;
                    FileInfo info = new FileInfo(path);

                    if (!File.Exists(path) || item.size != info.Length || item.hash != file.hash)
                    {
                        remove_list.Add(file.path);
                    }
                    else
                    {
                        string filename = Path.GetFileName(item.path);
                        if (filename[0] == '_' || index == rnd_index ||
                            File.GetLastWriteTime(path).Ticks > cached_time.Ticks)
                        {
                            if (index == rnd_index) {
                                rnd_index = rnd_list.Count > 0 ? rnd_list.Dequeue() : -1;
                            }

                            verify_file_list.Add(file.path);

                            MD5Async.Compute(ref path, ref item, delegate (string p, DownloadFileInfo f, bool is_match)
                            {
                                if (VerifyCallback != null)
                                    VerifyCallback(p);

                                verify_file_list.Remove(f.path);

                                if (is_match)
                                {
                                    list.Remove(f);
                                }
                                else
                                {
                                    remove_list.Add(f.path);
                                    verify_success = false;
                                }
                            });
                        }
                        else
                        {
                            list.Remove(item);
                        }
                    }
                }
                else
                {
                    remove_list.Add(file.path);
                }

                ++index;
            }

            while (verify_file_list.Count > 0)
            {
                yield return new WaitForSeconds(0.1f);
            }

            RemoveCachedList(remove_list);

            DebugUtils.Log("Random validation has {0}", (verify_success ? "succeeded" : "failed"));

            // Checks all local files
            if (!verify_success)
            {
                foreach (DownloadFileInfo file in cached_list_)
                {
                    DownloadFileInfo item = tmp_list.Find(i => i.path == file.path);
                    if (item != null)
                    {
                        verify_file_list.Add(file.path);

                        string path = target_path_ + file.path;
                        MD5Async.Compute(ref path, ref item, delegate (string p, DownloadFileInfo f, bool is_match)
                        {
                            if (VerifyCallback != null)
                                VerifyCallback(p);

                            verify_file_list.Remove(f.path);

                            if (!is_match)
                            {
                                remove_list.Add(f.path);

                                if (!list.Contains(f))
                                    list.Add(f);
                            }
                        });
                    }
                }

                while (verify_file_list.Count > 0)
                {
                    yield return new WaitForSeconds(0.1f);
                }

                RemoveCachedList(remove_list);
            }

            TimeSpan span = new TimeSpan(DateTime.Now.Ticks - check_time_.Ticks);
            DebugUtils.Log("File check total time - {0:F2}s", span.TotalMilliseconds / 1000f);

            total_download_count_ = list.Count;

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
                UpdateCachedList();

                state_ = State.Completed;
                DebugUtils.Log("All resources are up to date.");
                OnFinishedCallback(DownloadResult.SUCCESS);
            }
        }

        private void RemoveCachedList (List<string> remove_list)
        {
            if (remove_list.Count <= 0)
                return;

            cached_list_.RemoveAll(item => {
                return remove_list.Contains(item.path);
            });

            foreach (string path in remove_list) {
                delete_file_list_.Add(target_path_ + path);
            }

            remove_list.Clear();
        }

        private void DownloadListFile (string url)
        {
            bool failed = false;
            try
            {
                // Request a list of download files.
                DebugUtils.Log("Getting list file from {0}", url);
                web_client_.DownloadDataAsync(new Uri(url));
            }
            catch (Exception e)
            {
                DebugUtils.Log("Failure in DownloadListFile: {0}", e.ToString());
                failed = true;
            }

            if (failed)
            {
                Stop();
            }
        }

        private void DeleteLocalFiles ()
        {
            if (delete_file_list_.Count <= 0)
                return;

            foreach (string path in delete_file_list_)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    DebugUtils.Log("Deleted resource file \npath: {0}", path);
                }
            }

            delete_file_list_.Clear();
        }

        // Downloading files.
        private void DownloadResourceFile ()
        {
            if (download_list_.Count <= 0)
            {
                UpdateCachedList();

                TimeSpan span = new TimeSpan(DateTime.Now.Ticks - check_time_.Ticks);
                DebugUtils.Log("File download total time - {0:F2}s", span.TotalMilliseconds / 1000f);

                state_ = State.Completed;
                DebugUtils.Log("Download completed.");
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
                DebugUtils.Log("Download file - {0}", file_path);
                retry_download_count_ = 0;
                cur_download_path_ = Path.GetDirectoryName(file_path);
                cur_download_path_ += "/" + Path.GetRandomFileName();

                web_client_.DownloadFileAsync(new Uri(host_url_ + info.path), cur_download_path_, info);
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
                    DebugUtils.Log("Exception Error: {0}", ar.Error);
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

                    //DebugUtils.Log("Json data >> {0}", data);

                    // Redirect url
                    if (json.ContainsKey("url"))
                    {
                        string url = json["url"] as string;
                        if (url[url.Length - 1] != '/')
                            url += "/";

                        host_url_ = url;
                        DebugUtils.Log("Redirect download url: {0}", host_url_);
                    }

                    List<object> list = json["data"] as List<object>;
                    if (list.Count <= 0)
                    {
                        DebugUtils.Log("Invalid list data. List count is 0.");
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
                                info.hash_front = node["md5_front"] as string;
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
                DebugUtils.Log("Failure in DownloadDataCompleteCb: {0}", e.ToString());
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
                    DebugUtils.Log("Exception Error: {0}", ar.Error);
                    DebugUtils.Assert(false);
                    failed = true;
                }
                else
                {
                    var info = (DownloadFileInfo)ar.UserState;
                    if (info == null)
                    {
                        DebugUtils.Log("DownloadFileInfo object is null.");
                        failed = true;
                    }
                    else
                    {
                        string path = target_path_ + info.path;
                        File.Move(cur_download_path_, path);

                        ++cur_download_count_;
                        cur_download_size_ += info.size;
                        download_list_.Remove(info);
                        cached_list_.Add(info);

                        DownloadResourceFile();
                    }
                }
            }
            catch (Exception e)
            {
                DebugUtils.Log("Failure in DownloadFileCompleteCb: {0}", e.ToString());
                failed = true;
            }
            finally
            {
                mutex_.ReleaseMutex();
            }

            if (failed)
            {
                web_client_.Dispose();
                File.Delete(cur_download_path_);

                if (retry_download_count_ < kMaxRetryCount)
                {
                    ++retry_download_count_;
                    FunapiManager.instance.StartCoroutine(AsyncDownloadFile());
                }
                else
                {
                    Stop();
                }
            }
        }

        IEnumerator AsyncDownloadFile ()
        {
            float time = 1f;
            for (int i = 0; i < retry_download_count_; ++i)
                time *= 2f;
            yield return new WaitForSeconds(time);

            DownloadResourceFile();
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
        private static readonly string kCachedFileName = "cached_files";
        private static readonly int kMaxRetryCount = 3;

        // member variables.
        private State state_ = State.None;
        private string host_url_ = "";
        private string target_path_ = "";
        private string cur_download_path_ = "";
        private int cur_download_count_ = 0;
        private int total_download_count_ = 0;
        private UInt64 cur_download_size_ = 0;
        private UInt64 total_download_size_ = 0;
        private int retry_download_count_ = 0;

        private FunapiManager manager_ = null;
        private Mutex mutex_ = new Mutex();
        private DateTime check_time_;
        private WebClient web_client_ = new WebClient();
        private List<DownloadFileInfo> cached_list_ = new List<DownloadFileInfo>();
        private List<DownloadFileInfo> download_list_ = new List<DownloadFileInfo>();
        private List<string> delete_file_list_ = new List<string>();
    }
}
