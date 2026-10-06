#if UNITY_EDITOR
using System.Collections.Generic;
using Ezg.Editor.Shared.EzgKit.Pages;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Danh mục trang theo đúng thứ tự hiện ở cột trái. Thêm trang mới = thêm một dòng ở đây (id phải ổn
    ///     định — <c>/setup-project</c> gọi theo id).
    /// </summary>
    internal static class SetupPages
    {
        /// <summary>Tạo bộ trang mới (mỗi cửa sổ / mỗi lời gọi API một bộ — trang giữ state UI riêng).</summary>
        internal static List<SetupPage> Create() =>
            new()
            {
                new OverviewPage(),
                new ProjectInfoPage(),
                new MarketingPage(),
                new AdsPage(),
                new IapPage(),
                new ArtStylePage(),
                new LocalizePage(),
                new FirebasePage(),
                new PublisherPage(),
                new SocialPage(),
            };

        internal static SetupPage Find(List<SetupPage> pages, string id)
        {
            foreach (var page in pages)
                if (page.Id == id)
                    return page;
            return null;
        }
    }
}
#endif
