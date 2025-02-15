﻿using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;
using NewLife;
using NewLife.Cube;
using NewLife.Cube.ViewModels;
using NewLife.Log;
using NewLife.Web;
using Stardust.Data;
using Stardust.Data.Monitors;
using Stardust.Server.Services;
using XCode;
using XCode.Membership;

namespace Stardust.Web.Areas.Monitors.Controllers
{
    [Menu(90)]
    [MonitorsArea]
    public class AppTracerController : EntityController<AppTracer>
    {
        private readonly ITraceItemStatService _traceItemStatService;

        static AppTracerController()
        {
            LogOnChange = true;

            ListFields.RemoveField("AppId", "TimeoutExcludes", "VipClients", "Nodes", "AlarmRobot");
            ListFields.RemoveCreateField();

            {
                var df = ListFields.AddListField("DayMonitor", null, "Category");
                df.Header = "每日监控";
                df.DisplayName = "每日监控";
                df.Title = "该应用每日监控数据";
                df.Url = "/Monitors/AppDaystat?appId={ID}";
            }

            {
                var df = ListFields.GetField("ItemCount") as ListField;
                //df.Header = "每日监控";
                df.DisplayName = "{ItemCount}";
                //df.Title = "该应用每日监控数据";
                df.Url = "/Monitors/TraceItem?appId={ID}";
            }

            {
                var df = ListFields.AddListField("Online", "UpdateUser");
                df.Header = "在线实例";
                df.DisplayName = "在线实例";
                df.Title = "查看该应用的在线实例应用";
                df.Url = "/registry/AppOnline?appId={AppId}";
                df.DataVisible = e => e is AppTracer entity && entity.AppId > 0;
            }

            {
                var df = ListFields.AddListField("Meter", "UpdateUser");
                df.DisplayName = "性能";
                df.Header = "性能";
                df.Url = "/Registry/AppMeter?appId={ID}";
            }

            {
                var df = ListFields.AddListField("Log", "UpdateUser");
                df.DisplayName = "修改日志";
                df.Header = "修改日志";
                df.Url = "/Admin/Log?category=应用跟踪器&linkId={ID}";
            }
        }

        public AppTracerController(ITraceItemStatService traceItemStatService) => _traceItemStatService = traceItemStatService;

        protected override IEnumerable<AppTracer> Search(Pager p)
        {
            var id = p["Id"].ToInt(-1);
            if (id > 0)
            {
                var app = AppTracer.FindByID(id);
                if (app != null) return new[] { app };
            }
            var appId = p["appId"].ToInt(-1);
            if (appId > 0)
            {
                var entity = AppTracer.FindByAppId(appId);
                if (entity != null) return new List<AppTracer> { entity };
            }

            var category = p["category"];
            var enable = p["enable"]?.ToBoolean();

            var start = p["dtStart"].ToDateTime();
            var end = p["dtEnd"].ToDateTime();

            p.RetrieveState = true;

            return AppTracer.Search(category, enable, start, end, p["Q"], p);
        }

        protected override Int32 OnDelete(AppTracer entity)
        {
            using var tran = AppTracer.Meta.CreateTrans();

            var rs = base.OnDelete(entity);

            var list = AppDayStat.FindAllByAppId(entity.ID);
            list.Delete();

            //var list2 = TraceDayStat.FindAllByAppId(entity.ID);
            //list2.Delete();
            TraceDayStat.DeleteByAppAndItem(entity.ID, 0);

            tran.Commit();

            return rs;
        }

        protected override Boolean Valid(AppTracer entity, DataObjectMethodType type, Boolean post)
        {
            if (post)
            {
                // 更新时关联应用
                switch (type)
                {
                    case DataObjectMethodType.Update:
                    case DataObjectMethodType.Insert:
                        if (entity.AppId == 0)
                        {
                            var app = App.FindByName(entity.Name);
                            if (app != null) entity.AppId = app.Id;
                        }

                        break;
                }

                // 修正埋点数
                switch (type)
                {
                    case DataObjectMethodType.Update:
                        entity.Fix();
                        entity.Update();

                        break;
                }
            }

            var rs = base.Valid(entity, type, post);

            if (post && type == DataObjectMethodType.Delete)
            {
                var list = AppDayStat.FindAllByAppId(entity.ID);
                list.Delete();

                var list2 = TraceItem.FindAllByApp(entity.ID);
                list2.Delete();

                TraceDayStat.DeleteByAppAndItem(entity.ID, 0);
            }

            return rs;
        }

        [EntityAuthorize(PermissionFlags.Update)]
        public ActionResult Fix()
        {
            foreach (var item in SelectKeys)
            {
                var app = AppTracer.FindByID(item.ToInt());
                if (app != null)
                {
                    XTrace.WriteLine("修正 {0}/{1}", app.Name, app.ID);

                    //_traceItemStatService.Add(app.ID);
                    _traceItemStatService.Process(app.ID, 30);

                    app.Fix();
                    app.Update();
                }
            }

            return JsonRefresh("成功！");
        }

        [EntityAuthorize(PermissionFlags.Update)]
        public ActionResult FixOldData()
        {
            foreach (var item in SelectKeys)
            {
                var app = AppTracer.FindByID(item.ToInt());
                if (app != null)
                {
                    XTrace.WriteLine("修正旧数据 {0}/{1}", app.Name, app.ID);

                    {
                        var list = TraceDayStat.FindAllByAppId(app.ID);
                        foreach (var st in list)
                        {
                            var ti = app.GetOrAddItem(st.Name);
                            if (st.ItemId == 0 && !st.Name.IsNullOrEmpty())
                            {
                                st.ItemId = ti.Id;
                                st.SaveAsync();
                            }
                            if (ti.CreateTime.Year < 2000 || ti.CreateTime > st.CreateTime)
                            {
                                ti.CreateTime = st.CreateTime;
                                ti.SaveAsync(3_000);
                            }
                        }

                        app.Days = list.DistinctBy(e => e.StatDate.Date).Count();
                        app.Total = list.Sum(e => e.Total);
                    }
                    {
                        var list = TraceHourStat.FindAllByAppId(app.ID);
                        foreach (var st in list)
                        {
                            if (st.ItemId == 0 && !st.Name.IsNullOrEmpty())
                            {
                                var ti = app.GetOrAddItem(st.Name);
                                st.ItemId = ti.Id;
                                st.SaveAsync();
                            }
                        }
                    }

                    app.ItemCount = app.TraceItems.Count;
                    app.Update();
                }
            }

            return JsonRefresh("成功！");
        }
    }
}