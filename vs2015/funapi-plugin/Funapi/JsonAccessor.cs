// Copyright 2013-2016 iFunFactory Inc. All Rights Reserved.
//
// This work is confidential and proprietary to iFunFactory Inc. and
// must not be used, disclosed, copied, or distributed without the prior
// consent of iFunFactory Inc.

using MiniJSON;
using System;
using System.Collections.Generic;


namespace Fun
{
    // Container to hold json-related functions.
    public abstract class JsonAccessor
    {
        public abstract object Clone (object json);

        public abstract string Serialize (object json);
        public abstract object Deserialize (string json_str);

        public abstract bool HasField (object json, string field_name);
        public abstract void RemoveField (object json, string field_name);

        public abstract int GetArrayCount (object json);
        public abstract object GetArrayObject (object json, int index);

        public abstract object GetObject (object json, string field_name);
        public abstract void SetObject (object json, string field_name, object value);

        public abstract string GetStringField (object json, string field_name);
        public abstract void SetStringField (object json, string field_name, string value);

        public abstract Int64 GetIntegerField (object json, string field_name);
        public abstract void SetIntegerField (object json, string field_name, Int64 value);

        public abstract bool GetBooleanField (object json, string field_name);
        public abstract void SetBooleanField (object json, string field_name, bool value);
    }


    // Default json accessor
    public class DictionaryJsonAccessor : JsonAccessor
    {
        public override object Clone (object json)
        {
            return new Dictionary<string, object>(getDic(json));
        }

        public override string Serialize (object json)
        {
            return Json.Serialize(json);
        }

        public override object Deserialize (string json_string)
        {
            return Json.Deserialize(json_string);
        }

        public override bool HasField (object json, string field_name)
        {
            return getDic(json).ContainsKey(field_name);
        }

        public override void RemoveField (object json, string field_name)
        {
            getDic(json).Remove(field_name);
        }

        public override int GetArrayCount (object json)
        {
            return getList(json).Count;
        }

        public override object GetArrayObject (object json, int index)
        {
            return getList(json)[index];
        }

        public override object GetObject (object json, string field_name)
        {
            return getDic(json)[field_name];
        }

        public override void SetObject (object json, string field_name, object value)
        {
            getDic(json)[field_name] = value;
        }

        public override string GetStringField (object json, string field_name)
        {
            return getDic(json)[field_name] as string;
        }

        public override void SetStringField (object json, string field_name, string value)
        {
            getDic(json)[field_name] = value;
        }

        public override Int64 GetIntegerField (object json, string field_name)
        {
            return Convert.ToInt64(getDic(json)[field_name]);
        }

        public override void SetIntegerField (object json, string field_name, Int64 value)
        {
            getDic(json)[field_name] = value;
        }

        public override bool GetBooleanField (object json, string field_name)
        {
            return Convert.ToBoolean(getDic(json)[field_name]);
        }

        public override void SetBooleanField (object json, string field_name, bool value)
        {
            getDic(json)[field_name] = value;
        }


        List<object> getList (object json)
        {
            List<object> list = json as List<object>;
            FunDebug.Assert(list != null);
            return list;
        }

        Dictionary<string, object> getDic (object json)
        {
            Dictionary<string, object> dic = json as Dictionary<string, object>;
            FunDebug.Assert(dic != null);
            return dic;
        }
    }

}  // namespace Fun
