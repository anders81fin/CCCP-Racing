using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>The archive appears halfway through the launch banner's descent.</summary>
public partial class Hud
{
    const float VideoHoldTime = 2f, BannerSlideTime = 0.8f;
    const float StartupEndTime = VideoHoldTime + BannerSlideTime;
    const float ArchiveStartTime = VideoHoldTime + BannerSlideTime * 0.5f;
    float _startupBannerAge = -1f;

    public bool StartupBannerActive => _startupBannerAge >= 0 && _startupBannerAge < StartupEndTime;
    public bool StartupVideoOnly => _startupBannerAge >= 0 && _startupBannerAge < VideoHoldTime;
    public bool StartupArchivePending => _startupBannerAge >= 0 && _startupBannerAge < ArchiveStartTime;

    public void BeginStartupBanner()
    {
        _startupBannerAge = 0;
        _menuTextClock = 0;
    }

    void TickStartupBanner(float delta)
    {
        if (!StartupBannerActive) return;
        _startupBannerAge += Math.Min(delta, 0.05f);
        // Start the heading's typewriter only when the archive panel appears.
        if (StartupArchivePending) _menuTextClock = 0;
    }

    float StartupBackdropOpacity => _startupBannerAge < 0 ? 1f
        : Mathf.SmoothStep(0f, 1f, Math.Clamp((_startupBannerAge - VideoHoldTime) / BannerSlideTime, 0f, 1f));

    float StartupBannerOffset
    {
        get
        {
            if (_startupBannerAge < 0) return 0;
            float t = Math.Clamp((_startupBannerAge - VideoHoldTime) / BannerSlideTime, 0f, 1f);
            // An even glide with no slowing at the end: at the low resolution an eased finish creeps
            // the last pixels one by one, which reads as the banner sticking.
            return -68f * (1f - t);
        }
    }
}
