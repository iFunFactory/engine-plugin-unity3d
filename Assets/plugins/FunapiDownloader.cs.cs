// Copyright (C) 2013 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using UnityEngine;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SimpleJSON;


namespace Fun
{
    public class FunapiHttpDownloader
    {
        #region public interface
        public FunapiHttpDownloader (string target_path, OnUpdate on_update, OnFinished on_finished)
        {
            state_ = State.Ready;

            target_path_ = target_path;
            if (target_path_[target_path_.Length - 1] != '/')
                target_path_ += "/";

            on_update_ = on_update;
            on_finished_ = on_finished;

            // List file handler
            web_client_.DownloadDataCompleted += new DownloadDataCompletedEventHandler(DownloadDataCompleteCb);

            // Download file handler
            web_client_.DownloadProgressChanged += new DownloadProgressChangedEventHandler(DownloadProgressChangedCb);
            web_client_.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCompleteCb);

            // Load file list
            LoadHaveList();
        }

        // Start downloading
        public void StartDownload (string hostname_or_ip, UInt16 port, string suffix_path, bool https = false)
        {
            string url = "http://";
            if (https)
                url = "https://";
            url += hostname_or_ip + ":" + port;
            url += "/v" + kCurrentFunapiProtocolVersion + "/";
            url += suffix_path;

            StartDownload(url);
        }

        public void StartDownload (string url)
        {
            mutex_.WaitOne();

            try
            {
                string ver = "/v" + kCurrentFunapiProtocolVersion + "/";
                int index = url.IndexOf(ver);
                if (index <= 0)
                {
                    Debug.LogError("Invalid request url : " + url);
                    DebugUtils.Assert(false);
                    return;
                }

                host_url_ = url.Substring(0, index + ver.Length);

                if (state_ == State.Downloading)
                {
                    DownloadUrl info = new DownloadUrl();
                    info.host = host_url_;
                    info.url = url;
                    url_list_.Add(info);
                    return;
                }

                Debug.Log("Start Download.");
                state_ = State.Downloading;

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

            if (state_ == State.Downloading)
            {
                if (on_finished_ != null)
                    on_finished_(DownloadResult.FAILED);
            }

            state_ = State.Ready;
            url_list_.Clear();
            download_list_.Clear();

            mutex_.ReleaseMutex();
        }
        #endregion

        #region internal implementation
        // Load file's information.
        private void LoadHaveList ()
        {
            try
            {
                cached_files_list_.Clear();

                string path = target_path_ + kSaveFile;
                if (!File.Exists(path))
                    return;

                StreamReader stream = File.OpenText(path);
                string data = stream.ReadToEnd();
                stream.Close();

                if (data.Length <= 0)
                {
                    Debug.LogWarning("Failed to get a file list.");
                    DebugUtils.Assert(false);
                    return;
                }

                JSONNode json = JSON.Parse(data);
                DebugUtils.Assert(json is JSONClass);

                JSONArray list = json["data"].AsArray;

                foreach (JSONNode node in list)
                {
                    DownloadFile info = new DownloadFile();
                    info.path = node["path"];
                    info.md5 = node["md5"];

                    cached_files_list_.Add(info);
                }

                Debug.Log("Load file's information : " + cached_files_list_.Count);
            }
            catch (Exception e)
            {
                Debug.Log("Failure in LoadHaveList: " + e.ToString());
            }
        }

        // Save file's information.
        private void SaveHaveList ()
        {
            try
            {
                string data = "{ \"data\": [ ";
                int index = 0;

                foreach (DownloadFile item in cached_files_list_)
                {
                    data += "{ \"path\":\"" + item.path + "\", ";
                    data += "\"md5\":\"" + item.md5 + "\" }";

                    ++index;
                    if (index < cached_files_list_.Count)
                        data += ", ";
                }

                data += " ] }";
                Debug.Log("SAVE DATA: " + data);

                string path = target_path_ + kSaveFile;
                FileStream file = File.Open(path, FileMode.Create);
                StreamWriter stream = new StreamWriter(file);
                stream.Write(data);
                stream.Flush();
                stream.Close();

                Debug.Log("Save file's information : " + cached_files_list_.Count);
            }
            catch (Exception e)
            {
                Debug.Log("Failure in SaveHaveList: " + e.ToString());
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
            DateTime list_file_time = File.GetLastWriteTime(target_path_ + kSaveFile);

            foreach (DownloadFile item in list)
            {
                DownloadFile info = cached_files_list_.Find(i => i.path == item.path);
                if (info != null)
                {
                    bool is_same = true;
                    if (info.md5 != item.md5)
                    {
                        is_same = false;
                    }
                    else
                    {
                        DateTime time = File.GetLastWriteTime(target_path_ + info.path);
                        if (time.Ticks > list_file_time.Ticks)
                            is_same = false;
                    }

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

                    SaveHaveList();
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

                // Request a file.
                web_client_.DownloadFileAsync(new Uri(host_url_ + cur_download_.path), target_path_ + cur_download_.path);
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
                    Debug.LogError("Exception Error: " + ar.Error);
                    DebugUtils.Assert(false);
                    failed = true;
                }
                else
                {
                    // Parse json
                    string data = Encoding.ASCII.GetString(ar.Result);
                    JSONNode json = JSON.Parse(data);
                    DebugUtils.Assert(json is JSONClass);

                    Debug.Log("Json data >>  " + data);

                    JSONArray list = json["data"].AsArray;
                    if (list.Count <= 0)
                    {
                        Debug.LogWarning("Invalid list data. List count is 0.");
                        DebugUtils.Assert(false);
                        failed = true;
                    }
                    else
                    {
                        foreach (JSONNode node in list)
                        {
                            DownloadFile info = new DownloadFile();
                            info.path = node["path"];
                            info.md5 = node["md5"];

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
                    Debug.LogError("Exception Error: " + ar.Error);
                    DebugUtils.Assert(false);
                    failed = true;
                }
                else
                {
                    if (download_list_.Count > 0)
                    {
                        cur_download_.md5 = GetMd5Hash(target_path_ + cur_download_.path);
                        cached_files_list_.Add(cur_download_);

                        download_list_.RemoveAt(0);
                    }

                    DownloadResourceFile();
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

        private string GetMd5Hash (string path)
        {
            if (!File.Exists(path))
            {
                Debug.Log("GetMd5Hash - Can't find a file. path: " + path);
                return "";
            }

            FileStream file = File.OpenRead(path);
            byte[] buf = new byte[file.Length];
            file.Read(buf, 0, (int)file.Length);
            file.Close();

            byte[] data = md5_.ComputeHash(buf);
            buf = null;

            string md5hash = "";
            foreach (byte n in data)
                md5hash += n.ToString("x2");

            return md5hash;
        }
        #endregion


        enum State
        {
            Ready,
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

        // Funapi Version
        private static readonly int kCurrentFunapiProtocolVersion = 1;

        // Save file-related constants.
        private readonly string kSaveFile = "cached_files_list";

        // member variables.
        private Mutex mutex_ = new Mutex();
        private MD5 md5_ = MD5.Create();
        private State state_ = State.Ready;
        private WebClient web_client_ = new WebClient();
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
