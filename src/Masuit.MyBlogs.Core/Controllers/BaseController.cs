﻿using AutoMapper;
using AutoMapper.QueryableExtensions;
using EFCoreSecondLevelCacheInterceptor;
using Masuit.MyBlogs.Core.Common;
using Masuit.MyBlogs.Core.Common.Mails;
using Masuit.MyBlogs.Core.Configs;
using Masuit.MyBlogs.Core.Extensions;
using Masuit.MyBlogs.Core.Extensions.Firewall;
using Masuit.MyBlogs.Core.Infrastructure.Services.Interface;
using Masuit.MyBlogs.Core.Models.DTO;
using Masuit.MyBlogs.Core.Models.Entity;
using Masuit.MyBlogs.Core.Models.Enum;
using Masuit.MyBlogs.Core.Models.ViewModel;
using Masuit.Tools;
using Masuit.Tools.Core.Net;
using Masuit.Tools.Security;
using Masuit.Tools.Strings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using System.Linq.Expressions;
using System.Net;
using System.Text.RegularExpressions;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace Masuit.MyBlogs.Core.Controllers
{
    /// <summary>
    /// 基本父控制器
    /// </summary>
    [ApiExplorerSettings(IgnoreApi = true), ServiceFilter(typeof(FirewallAttribute))]
    public class BaseController : Controller
    {
        /// <summary>
        /// UserInfoService
        /// </summary>
        public IUserInfoService UserInfoService { get; set; }

        /// <summary>
        /// MenuService
        /// </summary>
        public IMenuService MenuService { get; set; }

        /// <summary>
        /// LinksService
        /// </summary>
        public ILinksService LinksService { get; set; }

        public IAdvertisementService AdsService { get; set; }
        public IVariablesService VariablesService { get; set; }

        public UserInfoDto CurrentUser => HttpContext.Session.Get<UserInfoDto>(SessionKey.UserInfo) ?? new UserInfoDto();

        /// <summary>
        /// 客户端的真实IP
        /// </summary>
        public string ClientIP => HttpContext.Connection.RemoteIpAddress.ToString();

        /// <summary>
        /// 普通访客是否token合法
        /// </summary>
        public bool VisitorTokenValid => Request.Cookies["Email"].MDString3(AppConfig.BaiduAK).Equals(Request.Cookies["FullAccessToken"]);


        public IMapper Mapper { get; set; }
        public MapperConfiguration MapperConfig { get; set; }

        /// <summary>
        /// 响应数据
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="success">响应状态</param>
        /// <param name="message">响应消息</param>
        /// <param name="isLogin">登录状态</param>
        /// <param name="code">http响应码</param>
        /// <returns></returns>
        public ActionResult ResultData(object data, bool success = true, string message = "", bool isLogin = true, HttpStatusCode code = HttpStatusCode.OK)
        {
            return Ok(new
            {
                IsLogin = isLogin,
                Success = success,
                Message = message,
                Data = data,
                code
            });
        }

        protected string ReplaceVariables(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var keys = Regex.Matches(text, @"\{\{[\w._-]+\}\}").Select(m => m.Value).Join(",");
            if (!string.IsNullOrEmpty(keys))
            {
                var dic = VariablesService.GetQueryFromCache(v => keys.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value);
                var template = Template.Create(text);
                foreach (var (key, value) in dic)
                {
                    template.Set(key, value);
                }

                return template.Render();
            }

            return text;
        }

        /// <summary>在调用操作方法前调用。</summary>
        /// <param name="filterContext">有关当前请求和操作的信息。</param>
        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            ViewBag.Desc = CommonHelper.SystemSettings["Description"];
            var user = filterContext.HttpContext.Session.Get<UserInfoDto>(SessionKey.UserInfo);
#if DEBUG
            user = UserInfoService.GetByUsername("masuit").Mapper<UserInfoDto>();
            filterContext.HttpContext.Session.Set(SessionKey.UserInfo, user);
#endif
            if (CommonHelper.SystemSettings.GetOrAdd("CloseSite", "false") == "true" && user?.IsAdmin != true)
            {
                filterContext.Result = RedirectToAction("ComingSoon", "Error");
            }

            if (Request.Method == HttpMethods.Post && !Request.Path.Value.Contains("get", StringComparison.InvariantCultureIgnoreCase) && CommonHelper.SystemSettings.GetOrAdd("DataReadonly", "false") == "true" && !filterContext.Filters.Any(m => m.ToString().Contains(nameof(MyAuthorizeAttribute))))
            {
                filterContext.Result = ResultData("网站当前处于数据写保护状态，无法提交任何数据，如有疑问请联系网站管理员！", false, "网站当前处于数据写保护状态，无法提交任何数据，如有疑问请联系网站管理员！", user != null, HttpStatusCode.BadRequest);
            }

            if (user == null && Request.Cookies.ContainsKey("username") && Request.Cookies.ContainsKey("password")) //执行自动登录
            {
                var name = Request.Cookies["username"];
                var pwd = Request.Cookies["password"];
                var userInfo = UserInfoService.Login(name, pwd.DesDecrypt(AppConfig.BaiduAK));
                if (userInfo != null)
                {
                    Response.Cookies.Append("username", name, new CookieOptions
                    {
                        Expires = DateTime.Now.AddYears(1),
                        SameSite = SameSiteMode.Lax
                    });
                    Response.Cookies.Append("password", pwd, new CookieOptions
                    {
                        Expires = DateTime.Now.AddYears(1),
                        SameSite = SameSiteMode.Lax
                    });
                    filterContext.HttpContext.Session.Set(SessionKey.UserInfo, userInfo);
                }
            }

            if (ModelState.IsValid) return;
            var errmsgs = ModelState.SelectMany(kv => kv.Value.Errors.Select(e => e.ErrorMessage)).Select((s, i) => $"{i + 1}. {s}").ToList();
            filterContext.Result = true switch
            {
                _ when (Request.Headers[HeaderNames.Accept] + "").StartsWith("application/json") || Request.Method == HttpMethods.Post => ResultData(errmsgs, false, "数据校验失败，错误信息：" + errmsgs.Join(" | "), user != null, HttpStatusCode.BadRequest),
                _ => base.BadRequest("参数错误：" + errmsgs.Join(" | "))
            };
        }

        /// <summary>在调用操作方法后调用。</summary>
        /// <param name="filterContext">有关当前请求和操作的信息。</param>
        public override void OnActionExecuted(ActionExecutedContext filterContext)
        {
            if (filterContext.Result is ViewResult)
            {
                ViewBag.menus = MenuService.GetQueryFromCache(m => m.ParentId == null && m.Status == Status.Available).OrderBy(m => m.Sort).ToList(); //菜单
                var model = new PageFootViewModel //页脚
                {
                    Links = LinksService.GetQuery(l => l.Status == Status.Available).OrderByDescending(l => l.Recommend).ThenByDescending(l => l.Loopbacks.Count).Take(30).ProjectTo<LinksDto>(MapperConfig).Cacheable().ToList()
                };
                ViewBag.Footer = model;
            }
        }

        /// <summary>
        /// 验证邮箱验证码
        /// </summary>
        /// <param name="mailSender"></param>
        /// <param name="email">邮箱地址</param>
        /// <param name="code">验证码</param>
        /// <returns></returns>
        internal async Task<string> ValidateEmailCode(IMailSender mailSender, string email, string code)
        {
            if (CurrentUser.IsAdmin)
            {
                return string.Empty; ;
            }

            if (string.IsNullOrEmpty(Request.Cookies["ValidateKey"]))
            {
                if (string.IsNullOrEmpty(code))
                {
                    return "请输入验证码！";
                }
                if (await RedisHelper.GetAsync("code:" + email) != code)
                {
                    return "验证码错误！";
                }
            }
            else if (Request.Cookies["ValidateKey"].DesDecrypt(AppConfig.BaiduAK) != email)
            {
                Response.Cookies.Delete("Email");
                Response.Cookies.Delete("NickName");
                Response.Cookies.Delete("ValidateKey");
                return "邮箱验证信息已失效，请刷新页面后重新评论！";
            }

            if (mailSender.HasBounced(email))
            {
                Response.Cookies.Delete("Email");
                Response.Cookies.Delete("NickName");
                Response.Cookies.Delete("ValidateKey");
                return "邮箱地址错误，请刷新页面后重新使用有效的邮箱地址！";
            }

            return string.Empty;
        }

        internal void WriteEmailKeyCookie(string email)
        {
            Response.Cookies.Append("Email", email, new CookieOptions()
            {
                Expires = DateTimeOffset.Now.AddYears(1),
                SameSite = SameSiteMode.Lax
            });
            Response.Cookies.Append("ValidateKey", email.DesEncrypt(AppConfig.BaiduAK), new CookieOptions()
            {
                Expires = DateTimeOffset.Now.AddYears(1),
                SameSite = SameSiteMode.Lax
            });
        }

        protected void CheckPermission(List<PostDto> posts)
        {
            if (CurrentUser.IsAdmin || VisitorTokenValid || Request.IsRobot())
            {
                return;
            }

            var location = Request.Location() + "|" + Request.Headers[HeaderNames.Referer] + "|" + Request.Headers[HeaderNames.UserAgent];
            if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip))
            {
                var s = rawip.Base64Decrypt();
                if (ClientIP != s)
                {
                    location += "|" + s.GetIPLocation();
                }
            }

            posts.RemoveAll(p =>
            {
                switch (p.LimitMode)
                {
                    case RegionLimitMode.AllowRegion:
                        return !Regex.IsMatch(location, p.Regions);
                    case RegionLimitMode.ForbidRegion:
                        return Regex.IsMatch(location, p.Regions);
                    case RegionLimitMode.AllowRegionExceptForbidRegion:
                        if (Regex.IsMatch(location, p.ExceptRegions))
                        {
                            return true;
                        }

                        goto case RegionLimitMode.AllowRegion;
                    case RegionLimitMode.ForbidRegionExceptAllowRegion:
                        if (Regex.IsMatch(location, p.ExceptRegions))
                        {
                            return false;
                        }

                        goto case RegionLimitMode.ForbidRegion;
                    default:
                        return false;
                }
            });
        }

        protected Expression<Func<Post, bool>> PostBaseWhere()
        {
            if (CurrentUser.IsAdmin || VisitorTokenValid || Request.IsRobot())
            {
                return _ => true;
            }

            var location = Request.Location() + "|" + Request.Headers[HeaderNames.Referer] + "|" + Request.Headers[HeaderNames.UserAgent];
            if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip) && ClientIP != rawip)
            {
                var s = rawip.Base64Decrypt();
                if (ClientIP != s)
                {
                    location += "|" + s.GetIPLocation();
                }
            }

            return p => p.LimitMode == null || p.LimitMode == RegionLimitMode.All ? true :
                   p.LimitMode == RegionLimitMode.AllowRegion ? Regex.IsMatch(location, p.Regions) :
                   p.LimitMode == RegionLimitMode.ForbidRegion ? !Regex.IsMatch(location, p.Regions) :
                   p.LimitMode == RegionLimitMode.AllowRegionExceptForbidRegion ? Regex.IsMatch(location, p.Regions) && !Regex.IsMatch(location, p.ExceptRegions) : !Regex.IsMatch(location, p.Regions) || Regex.IsMatch(location, p.ExceptRegions);
        }

        protected void CheckPermission(Post post)
        {
            if (CurrentUser.IsAdmin || VisitorTokenValid || Request.IsRobot())
            {
                return;
            }

            var location = Request.Location() + "|" + Request.Headers[HeaderNames.Referer] + "|" + Request.Headers[HeaderNames.UserAgent];
            if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip) && ClientIP != rawip)
            {
                var s = rawip.Base64Decrypt();
                if (ClientIP != s)
                {
                    location += "|" + s.GetIPLocation();
                }
            }

            switch (post.LimitMode)
            {
                case RegionLimitMode.AllowRegion:
                    if (!Regex.IsMatch(location, post.Regions))
                    {
                        Disallow(post);
                    }

                    break;
                case RegionLimitMode.ForbidRegion:
                    if (Regex.IsMatch(location, post.Regions))
                    {
                        Disallow(post);
                    }

                    break;
                case RegionLimitMode.AllowRegionExceptForbidRegion:
                    if (Regex.IsMatch(location, post.ExceptRegions))
                    {
                        Disallow(post);
                    }

                    goto case RegionLimitMode.AllowRegion;
                case RegionLimitMode.ForbidRegionExceptAllowRegion:
                    if (Regex.IsMatch(location, post.ExceptRegions))
                    {
                        break;
                    }

                    goto case RegionLimitMode.ForbidRegion;
            }
        }

        private void Disallow(Post post)
        {
            var remark = "无权限查看该文章";
            if (Request.Cookies.TryGetValue(SessionKey.RawIP, out var rawip) && ClientIP != rawip.Base64Decrypt())
            {
                remark += "，发生了IP切换，原始IP：" + rawip.Base64Decrypt();
            }

            RedisHelper.IncrBy("interceptCount");
            RedisHelper.LPush("intercept", new IpIntercepter()
            {
                IP = ClientIP,
                RequestUrl = $"//{Request.Host}/{post.Id}",
                Referer = Request.Headers[HeaderNames.Referer],
                Time = DateTime.Now,
                UserAgent = Request.Headers[HeaderNames.UserAgent],
                Remark = remark,
                Address = Request.Location(),
                HttpVersion = Request.Protocol,
                Headers = Request.Headers.ToJsonString()
            });
            throw new NotFoundException("文章未找到");
        }


    }
}