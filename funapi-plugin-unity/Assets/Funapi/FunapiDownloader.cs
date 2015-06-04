// Copyright (C) 2014 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using UnityEngine;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;


namespace Fun
{
    public class FunapiHttpDownloader
    {
        #region public interface
        public FunapiHttpDownloader (string target_path, OnUpdate on_update, OnFinished on_finished)
        {
            target_path_ = target_path;
            if (target_path_[target_path_.Length - 1] != '/')
                target_path_ += "/";
            target_path_ += kRootPath + "/";

            on_update_ = on_update;
            on_finished_ = on_finished;

            // List file handler
            web_client_.DownloadDataCompleted += new DownloadDataCompletedEventHandler(DownloadDataCompleteCb);

            // Download file handler
            web_client_.DownloadProgressChanged += new DownloadProgressChangedEventHandler(DownloadProgressChangedCb);
            web_client_.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCompleteCb);

            // Check file list
            CheckLocalFiles();
        }

        // Start downloading
        public void StartDownload (string hostname_or_ip, UInt16 port, bool https)
        {
            string url = String.Format("{0}://{1}:{2}",
                                       (https ? "https" : "http"), hostname_or_ip, port);

            StartDownload(url);
        }

        public void StartDownload (string url)
        {
            mutex_.WaitOne();

            try
            {
                string host_url = url;
                if (host_url[host_url.Length - 1] != '/')
                    host_url += "/";

                if (state_ == State.Downloading)
                {
                    DownloadUrl info = new DownloadUrl();
                    info.host = host_url;
                    info.url = url;
                    url_list_.Add(info);
                    return;
                }

                state_ = State.Start;
                host_url_ = host_url;
                Debug.Log("Start Download.");

                DownloadListFile(url);
            }
            finally
            {
                mutex_.ReleaseMutex();
            }
        }

        public void Stop()
        {
            mutex_.WaitOne();

            if (state_ == State.Start || state_ == State.Downloading)
            {
                web_client_.CancelAsync();

                if (on_finished_ != null)
                    on_finished_(DownloadResult.FAILED);
            }

            url_list_.Clear();
            download_list_.Clear();

            mutex_.ReleaseMutex();
        }

        public bool Connected
        {
            get { return state_ == State.Downloading || state_ == State.Completed; }
        }
        #endregion

        #region internal implementation
        // Load file's information.
        private void CheckLocalFiles ()
        {
            try
            {
                check_files_list_.Clear();
                cached_files_list_.Clear();

                if (!Directory.Exists(target_path_))
                    return;

                string[] files = Directory.GetFiles(target_path_, "*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    foreach (string s in files)
                    {
                        check_files_list_.Add(s);
                    }

                    MD5Async.Compute(check_files_list_[0], OnCheckMd5Finish);
                }
            }
            catch (Exception e)
            {
                Debug.Log("Failure in CheckLocalFiles: " + e.ToString());
            }
        }

        private void OnCheckMd5Finish (string md5hash)
        {
            DebugUtils.Assert(check_files_list_.Count > 0);
            string path = check_files_list_[0];
            path = path.Substring(target_path_.Length);

            DownloadFile info = new DownloadFile();
            info.path = path;
            info.md5 = md5hash;
            cached_files_list_.Add(info);

            check_files_list_.RemoveAt(0);
            if (check_files_list_.Count > 0)
            {
                MD5Async.Compute(check_files_list_[0], OnCheckMd5Finish);
            }
            else
            {
                Debug.Log("Check file's information : " + cached_files_list_.Count);
            }
        }

        // Check MD5
        private void CheckFileList (List<DownloadFile> list)
        {
            if (list.Count <= 0 || cached_files_list_.Count <= 0)
                return;

            List<DownloadFile> remove_list = new List<DownloadFile>();

            // Deletes local files
            foreach (DownloadFile item in cached_files_list_)
            {
                DownloadFile info = list.Find(i => i.path == item.path);
                if (info == null)
                {
                    remove_list.Add(item);
                    File.Delete(target_path_ + item.path);
                    Debug.Log("Deleted resource file. path: " + item.path);
                }
            }

            if (remove_list.Count > 0)
            {
                foreach (DownloadFile item in remove_list)
                {
                    cached_files_list_.Remove(item);
                }

                remove_list.Clear();
            }

            // Check download files
            foreach (DownloadFile item in list)
            {
                DownloadFile info = cached_files_list_.Find(i => i.path == item.path);
                if (info != null)
                {
                    bool is_same = info.md5 == item.md5;

                    if (is_same)
                        remove_list.Add(item);
                    else
                        cached_files_list_.Remove(info);
                }
            }

            if (remove_list.Count > 0)
            {
                foreach (DownloadFile item in remove_list)
                {
                    list.Remove(item);
                }

                remove_list.Clear();
            }

            remove_list = null;
        }

        private void DownloadListFile (string url)
        {
            bool failed = false;
            try
            {
                cur_download_ = null;

                // Request a list of download files.
                Debug.Log("List file url : " + url);
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

        // Downloading files.
        private void DownloadResourceFile ()
        {
            if (download_list_.Count <= 0)
            {
                if (url_list_.Count > 0)
                {
                    DownloadUrl info = url_list_[0];
                    url_list_.RemoveAt(0);

                    host_url_ = info.host;
                    DownloadListFile(info.url);
                }
                else
                {
                    state_ = State.Completed;
                    Debug.Log("Download completed.");

                    if (on_finished_ != null)
                        on_finished_(DownloadResult.SUCCESS);
                }
            }
            else
            {
                cur_download_ = download_list_[0];

                // Check directory
                string path = target_path_;
                int offset = cur_download_.path.LastIndexOf('/');
                if (offset >= 0)
                    path += cur_download_.path.Substring(0, offset);

                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                string file_path = target_path_ + cur_download_.path;
                if (File.Exists(file_path))
                    File.Delete(file_path);

                // Request a file.
                web_client_.DownloadFileAsync(new Uri(host_url_ + cur_download_.path), file_path);
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
                    // It can be null when CancelAsync() called in Stop().
                    if (ar.Result == null)
                    return;

                    state_ = State.Downloading;

                    // Parse json
                    string data = Encoding.ASCII.GetString(ar.Result);
                    Dictionary<string, object> json = Json.Deserialize(data) as Dictionary<string, object>;

                    Debug.Log("Json data >>  " + data);

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
                        foreach (Dictionary<string, object> node in list)
                        {
                            DownloadFile info = new DownloadFile();
                            info.path = node["path"] as string;
                            info.md5 = node["md5"] as string;

                            download_list_.Add(info);
                        }

                        // Check files
                        CheckFileList(download_list_);

                        // Download file.
                        DownloadResourceFile();
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
            if (cur_download_ == null || on_update_ == null)
                return;

            on_update_(cur_download_.path, ar.BytesReceived, ar.TotalBytesToReceive, ar.ProgressPercentage);
        }

        // Callback function for downloaded file.
        private void DownloadFileCompleteCb (object sender, System.ComponentModel.AsyncCompletedEventArgs ar)
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
                    if (download_list_.Count > 0)
                    {
                        cached_files_list_.Add(cur_download_);
                        download_list_.RemoveAt(0);

                        MD5Async.Compute(target_path_ + cur_download_.path, OnMd5Finish);
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

        private void OnMd5Finish (string md5hash)
        {
            cur_download_.md5 = md5hash;

            // Check next download file.
            DownloadResourceFile();
        }
        #endregion


        enum State
        {
            Ready,
            Start,
            Downloading,
            Completed
        }

        // Download url-related.
        class DownloadUrl
        {
            public string host;
            public string url;
        }

        // Download file-related.
        class DownloadFile
        {
            public string path;     // save file path
            public string md5;      // file's hash
        }

        public delegate void OnUpdate (string path, long bytes_received, long total_bytes, int percentage);
        public delegate void OnFinished (DownloadResult code);

        // Save file-related constants.
        private readonly string kRootPath = "client_data";

        // member variables.
        private Mutex mutex_ = new Mutex();
        private State state_ = State.Ready;
        private WebClient web_client_ = new WebClient();
        private List<string> check_files_list_ = new List<string>();
        private List<DownloadUrl> url_list_ = new List<DownloadUrl>();
        private List<DownloadFile> download_list_ = new List<DownloadFile>();
        private List<DownloadFile> cached_files_list_ = new List<DownloadFile>();
        private string host_url_ = "";
        private string target_path_ = "";
        private DownloadFile cur_download_ = null;
        private OnUpdate on_update_ = null;
        private OnFinished on_finished_ = null;
    }

    public enum DownloadResult
    {
        SUCCESS,
        FAILED
    }
}
