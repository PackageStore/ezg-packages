#if UNITY_EDITOR
namespace Ezg.Editor.Shared.Publisher.Profiles
{
    /// <summary>
    ///     SayGame — chỗ giữ sẵn. Chưa có tài liệu nên <see cref="RequiredSdks" /> rỗng: tab chỉ liệt kê
    ///     SDK đang có trong project và nói "chưa biết SayGame đòi gì", không bịa yêu cầu. Khi SayGame gửi
    ///     guide: điền <see cref="GuideUrl" />, <see cref="Intro" />, <see cref="RequiredSdks" /> theo mẫu
    ///     <see cref="NeptuneProfile" />.
    /// </summary>
    internal sealed class SayGameProfile : IPublisherProfile
    {
        public string Id => "saygame";
        public string Title => "SayGame";
        public string DisplayName => "SayGame";
        public string Subtitle => "Phát hành với SayGame — chưa có tài liệu, tab để trống chờ guide từ publisher.";
        public string GuideUrl => null;

        public string Intro =>
            "SayGame chưa gửi tài liệu (SDK bắt buộc, dev key, event). Khi có, điền RequiredSdks trong "
            + "SayGameProfile.cs (package com.ezg.ezgkit) theo mẫu NeptuneProfile: mỗi SDK một SdkRequirement, "
            + "ID publisher cấp dùng SdkIdSlot.Given, ID game tự tạo dùng SdkIdSlot.Own.";

        public SdkRequirement[] RequiredSdks => System.Array.Empty<SdkRequirement>();
    }
}
#endif
