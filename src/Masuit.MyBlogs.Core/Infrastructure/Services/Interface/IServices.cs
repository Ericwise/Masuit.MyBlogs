﻿using Masuit.MyBlogs.Core.Models.Entity;

namespace Masuit.MyBlogs.Core.Infrastructure.Services.Interface
{
    public partial interface IDonateService : IBaseService<Donate>
    { }

    public partial interface IFastShareService : IBaseService<FastShare>
    { }

    public partial interface IInternalMessageService : IBaseService<InternalMessage>
    { }

    public partial interface ILinksService : IBaseService<Links>
    { }

    public partial interface ILinkLoopbackService : IBaseService<LinkLoopback>
    { }

    public partial interface ILoginRecordService : IBaseService<LoginRecord>
    { }

    public partial interface IMiscService : IBaseService<Misc>
    { }

    public partial interface INoticeService : IBaseService<Notice>
    { }

    public partial interface IPostHistoryVersionService : IBaseService<PostHistoryVersion>
    { }

    public partial interface ISeminarService : IBaseService<Seminar>
    { }

    public partial interface ISystemSettingService : IBaseService<SystemSetting>
    { }

    public partial interface IPostMergeRequestService : IBaseService<PostMergeRequest>
    { }

    public partial interface IVariablesService : IBaseService<Variables>
    { }

    public partial interface IPostVisitRecordService : IBaseService<PostVisitRecord>
    { }

    public partial interface IAdvertisementClickRecordService : IBaseService<AdvertisementClickRecord>
    { }
}
