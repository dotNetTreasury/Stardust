﻿using System;
using System.Collections.Generic;
using NewLife.Configuration;
using NewLife.Data;
using NewLife.Log;
using NewLife.Serialization;
using Stardust.Models;

namespace Stardust.Configs
{
    internal class StarHttpConfigProvider : HttpConfigProvider
    {
        public ConfigInfo ConfigInfo { get; set; }

        private Boolean _useWorkerId;

        protected override IDictionary<String, Object> GetAll()
        {
            var rs = base.GetAll();

            if (rs != null && rs.Count > 0)
            {
                var inf = Info;
                ConfigInfo = JsonHelper.Convert<ConfigInfo>(inf);

                var dic = new Dictionary<String, Object>(inf);
                dic.Remove("configs");
                XTrace.WriteLine("从配置中心加载：{0}", dic.ToJson());
            }

            // 接收配置中心颁发的WorkerId
            if (rs != null && rs.TryGetValue("NewLife.WorkerId", out var wid))
            {
                if (Snowflake.GlobalWorkerId <= 0) _useWorkerId = true;

                var id = wid.ToInt();
                if (id > 0 && _useWorkerId)
                {
                    XTrace.WriteLine("配置中心为当前应用实例分配全局WorkerId={0}，保障雪花Id的唯一性", id);
                    Snowflake.GlobalWorkerId = id;
                }
            }

            return rs;
        }
    }
}