﻿using NewLife;
using NewLife.Configuration;
using NewLife.Data;
using NewLife.Log;
using NewLife.Remoting;
using NewLife.Serialization;
using Stardust.Models;
using Stardust.Registry;
using Stardust.Services;
using static System.Net.WebRequestMethods;

namespace Stardust.Configs;

internal class StarHttpConfigProvider : HttpConfigProvider
{
    public ConfigInfo ConfigInfo { get; set; }

    const String REGISTRY = "$Registry:";
    private Boolean _useWorkerId;

    protected override IDictionary<String, Object> GetAll()
    {
        try
        {
            var rs = base.GetAll();

            if (rs != null && rs.Count > 0)
            {
                var inf = Info;
                var ci = ConfigInfo = JsonHelper.Convert<ConfigInfo>(inf);

                if (ci != null && ci.Configs != null && ci.Configs.Count > 0)
                {
                    //var dic = new Dictionary<String, Object>(inf);
                    //dic.Remove("configs");
                    XTrace.WriteLine("从配置中心加载：{0}", ci.Configs.Keys.ToArray().Join());
                }
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
        catch (ApiException ex)
        {
            if (Client is ApiHttpClient http)
                http.Log?.Error(ex + "");
        }
        catch (Exception ex)
        {
            if (Client is ApiHttpClient http)
                http.Log?.Debug("配置中心[{0}]出错 {1}", http.Source, ex);
        }

        return null;
    }

#if !NET40
    private readonly HashSet<String> _keys = new();
    /// <summary>获取指定配置。拦截对注册中心的请求</summary>
    /// <param name="key"></param>
    /// <param name="createOnMiss"></param>
    /// <returns></returns>
    protected override IConfigSection Find(String key, Boolean createOnMiss)
    {
        if (key.StartsWithIgnoreCase(REGISTRY))
        {
            key = key.Substring(REGISTRY.Length);

            // 从注册中心获取服务
            if (Client is IRegistry registry)
            {
                var addrs = registry.ResolveAddressAsync(key).Result;

                // 注册服务有改变时，通知配置系统改变
                if (!_keys.Contains(key))
                {
                    _keys.Add(key);
                    registry.Bind(key, (k, ms) => NotifyChange());
                }

                return new ConfigSection { Key = key, Value = addrs.Join() };
            }
        }

        return base.Find(key, createOnMiss);
    }
#endif

    public void Attach(ICommandClient client) => client.RegisterCommand("config/publish", DoPublish);

    private String DoPublish(String argument)
    {
        // 临时采用反射办法。后面直接调用DoRefresh
        //var timer = this.GetValue("_timer") as TimerX;
        //if (timer != null) timer.SetNext(-1);
        //this.Invoke("DoRefresh", new Object[] { null });

        DoRefresh(null);

        return "刷新配置成功";
    }
}