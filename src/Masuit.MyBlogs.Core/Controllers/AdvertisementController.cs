﻿using AutoMapper.QueryableExtensions;
using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Infrastructure.Repository;
using Masuit.MyBlogs.Core.Infrastructure.Services.Interface;
using Masuit.MyBlogs.Core.Models.DTO;
using Masuit.MyBlogs.Core.Models.Entity;
using Masuit.MyBlogs.Core.Models.Enum;
using Masuit.MyBlogs.Core.Models.ViewModel;
using Masuit.Tools.Core.Net;
using Masuit.Tools.Linq;
using Masuit.Tools.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Net;
using System.Text.RegularExpressions;

namespace Masuit.MyBlogs.Core.Controllers
{
    [Route("partner/[action]")]
    public class AdvertisementController : BaseController
    {
        public IAdvertisementClickRecordService ClickRecordService { get; set; }

        /// <summary>
        /// 前往
        /// </summary>
        /// <param name="id">广告id</param>
        /// <returns></returns>
        [HttpGet("/p{id:int}"), HttpGet("{id:int}", Order = 1), ResponseCache(Duration = 3600)]
        public async Task<IActionResult> Redirect(int id)
        {
            var ad = await AdsService.GetByIdAsync(id) ?? throw new NotFoundException("推广链接不存在");
            if (!HttpContext.Request.IsRobot() && string.IsNullOrEmpty(HttpContext.Session.Get<string>("ads" + id)))
            {
                HttpContext.Session.Set("ads" + id, id.ToString());
                ad.ClickRecords.Add(new AdvertisementClickRecord()
                {
                    IP = ClientIP,
                    Location = ClientIP.GetIPLocation(),
                    Referer = Request.Headers[HeaderNames.Referer].ToString(),
                    Time = DateTime.Now
                });
                await AdsService.SaveChangesAsync();
                var start = DateTime.Today.AddMonths(-1);
                await ClickRecordService.GetQuery(a => a.Time < start).DeleteFromQueryAsync();
            }

            return Redirect(ad.Url);
        }

        /// <summary>
        /// 获取分页
        /// </summary>
        /// <returns></returns>
        [MyAuthorize]
        public async Task<ActionResult> GetPageData([FromServices] ICategoryService categoryService, [Range(1, int.MaxValue, ErrorMessage = "页数必须大于0")] int page = 1, [Range(1, int.MaxValue, ErrorMessage = "页大小必须大于0")] int size = 10, string kw = "")
        {
            Expression<Func<Advertisement, bool>> where = p => true;
            if (!string.IsNullOrEmpty(kw))
            {
                where = where.And(p => p.Title.Contains(kw) || p.Description.Contains(kw) || p.Url.Contains(kw));
            }

            var list = AdsService.GetQuery(where).OrderByDescending(p => p.Status == Status.Available).ThenByDescending(a => a.Price).ProjectTo<AdvertisementViewModel>(MapperConfig).ToPagedList(page, size);
            var cids = list.Data.Where(m => !string.IsNullOrEmpty(m.CategoryIds)).SelectMany(m => m.CategoryIds.Split(",", StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)).Distinct().ToArray();
            var dic = await categoryService.GetQuery(c => cids.Contains(c.Id)).ToDictionaryAsync(c => c.Id + "", c => c.Name);
            foreach (var ad in list.Data.Where(ad => !string.IsNullOrEmpty(ad.CategoryIds)))
            {
                ad.CategoryNames = JiebaNet.Segmenter.Common.Extensions.Join(ad.CategoryIds.Split(",").Select(c => dic.GetValueOrDefault(c)), ",");
            }

            return Ok(list);
        }

        /// <summary>
        /// 保存广告
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [HttpPost, MyAuthorize]
        public async Task<IActionResult> Save(AdvertisementDto model)
        {
            model.CategoryIds = model.CategoryIds?.Replace("null", "");
            model.Regions = Regex.Replace(model.Regions ?? "", @"(\p{P}|\p{Z}|\p{S})+", "|");
            if (model.RegionMode == RegionLimitMode.All)
            {
                model.Regions = null;
            }

            if (model.Types.Contains(AdvertiseType.Banner.ToString("D")) && string.IsNullOrEmpty(model.ImageUrl))
            {
                return ResultData(null, false, "宣传大图不能为空");
            }

            if (model.Types.Length > 3 && string.IsNullOrEmpty(model.ThumbImgUrl))
            {
                return ResultData(null, false, "宣传小图不能为空");
            }

            var b = await AdsService.AddOrUpdateSavedAsync(a => a.Id, model.Mapper<Advertisement>()) > 0;
            return ResultData(null, b, b ? "保存成功" : "保存失败");
        }

        /// <summary>
        /// 删除广告
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("{id}"), HttpGet("{id}"), MyAuthorize]
        public async Task<IActionResult> Delete(int id)
        {
            bool b = await AdsService.DeleteByIdAsync(id) > 0;
            return ResultData(null, b, b ? "删除成功" : "删除失败");
        }

        /// <summary>
        /// 广告上下架
        /// </summary>
        /// <param name="id">文章id</param>
        /// <returns></returns>
        [MyAuthorize, HttpPost("{id}")]
        public async Task<ActionResult> ChangeState(int id)
        {
            var ad = await AdsService.GetByIdAsync(id) ?? throw new NotFoundException("广告不存在！");
            ad.Status = ad.Status == Status.Available ? Status.Unavailable : Status.Available;
            return ResultData(null, await AdsService.SaveChangesAsync() > 0, ad.Status == Status.Available ? $"【{ad.Title}】已上架！" : $"【{ad.Title}】已下架！");
        }

        /// <summary>
        /// 随机前往一个广告
        /// </summary>
        /// <returns></returns>
        [HttpGet("/partner-random")]
        public async Task<ActionResult> RandomGo()
        {
            var ad = AdsService.GetByWeightedPrice((AdvertiseType)new Random().Next(1, 4), Request.Location());
            if (!HttpContext.Request.IsRobot() && string.IsNullOrEmpty(HttpContext.Session.Get<string>("ads" + ad.Id)))
            {
                HttpContext.Session.Set("ads" + ad.Id, ad.Id.ToString());
                ad.ClickRecords.Add(new AdvertisementClickRecord()
                {
                    IP = ClientIP,
                    Location = ClientIP.GetIPLocation(),
                    Referer = Request.Headers[HeaderNames.Referer].ToString(),
                    Time = DateTime.Now
                });
                await AdsService.SaveChangesAsync();
                var start = DateTime.Today.AddMonths(-1);
                await ClickRecordService.GetQuery(a => a.Time < start).DeleteFromQueryAsync();
            }

            return Redirect(ad.Url);
        }

        /// <summary>
        /// 广告访问记录
        /// </summary>
        /// <param name="id"></param>
        /// <param name="page"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        [HttpGet("/partner/{id}/records"), MyAuthorize]
        public async Task<IActionResult> ClickRecords(int id, int page = 1, int size = 15, string kw = "")
        {
            Expression<Func<AdvertisementClickRecord, bool>> where = e => e.AdvertisementId == id;
            if (!string.IsNullOrEmpty(kw))
            {
                kw = Regex.Escape(kw);
                where = where.And(e => Regex.IsMatch(e.IP + e.Location + e.Referer, kw));
            }

            var pages = await ClickRecordService.GetPagesAsync<DateTime, AdvertisementClickRecordViewModel>(page, size, where, e => e.Time, false);
            return Ok(pages);
        }

        /// <summary>
        /// 广告访问记录图表
        /// </summary>
        /// <param name="id"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        [HttpGet("/partner/{id}/records-chart"), MyAuthorize]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public async Task<IActionResult> ClickRecordsChart(int id, CancellationToken cancellationToken)
        {
            var list = await ClickRecordService.GetQuery(e => e.AdvertisementId == id, e => e.Time).Select(e => e.Time).GroupBy(t => t.Date).Select(g => new
            {
                Date = g.Key,
                Count = g.Count()
            }).ToListAsync(cancellationToken);
            return Ok(list);
        }

        /// <summary>
        /// 广告访问记录分析
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpGet("/partner/{id}/insight"), MyAuthorize]
        public IActionResult ClickRecordsInsight(int id)
        {
            return View(AdsService[id]);
        }
    }
}
