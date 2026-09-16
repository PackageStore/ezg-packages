using System;
using System.Collections.Generic;
using Ezg.Feature.Shared.Config;
using Ezg.Feature.Shared.Systems;
using Ezg.UserSegment;
using TigerForge;

namespace Ezg.Feature.System.UserSegment
{
    /// <summary>
    ///     screen_id của engine = tên <see cref="GameEnums.Features" /> dạng snake_case. Một nguồn duy nhất cho cả
    ///     manifest.screens lẫn hook SCREEN_OPEN (nghe <c>EventName.OnShowFeature</c>, data = FeatureType).
    /// </summary>
    public static class UserSegmentScreens
    {
        private static readonly Dictionary<GameEnums.Features, string> Cache = new Dictionary<GameEnums.Features, string>();

        public static string IdOf(GameEnums.Features feature)
        {
            if (!Cache.TryGetValue(feature, out var id))
            {
                id = feature.ToString().ToSnakeCase();
                Cache[feature] = id;
            }

            return id;
        }

        public static string[] All()
        {
            var list = new List<string>();
            foreach (GameEnums.Features f in Enum.GetValues(typeof(GameEnums.Features)))
            {
                if (f == GameEnums.Features.none) continue;
                var id = IdOf(f);
                if (!list.Contains(id)) list.Add(id);
            }

            return list.ToArray();
        }

        /// <summary>Listener của OnShowFeature. Im lặng nếu SDK chưa init hoặc event không mang FeatureType.</summary>
        public static void OnFeatureShown()
        {
            if (!SegmentationSdk.IsInitialized) return;
            var feature = EventManager.GetData<GameEnums.Features>(nameof(EventName.OnShowFeature));
            if (feature == GameEnums.Features.none) return;
            SegmentationSdk.ScreenOpen(IdOf(feature));
        }
    }
}
