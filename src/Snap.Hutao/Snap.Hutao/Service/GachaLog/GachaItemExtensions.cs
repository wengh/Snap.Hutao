// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.Interchange.GachaLog;
using Snap.Hutao.Service.Metadata;
using Snap.Hutao.Model.Metadata.Abstraction;
using Snap.Hutao.Service.Metadata.ContextAbstraction;
using Snap.Hutao.Web.Hoyolab.Hk4e.Event.GachaInfo;

namespace Snap.Hutao.Service.GachaLog;

internal static class GachaItemExtensions
{
    public static UIGFItem ToUIGFItem(this GachaItem gachaItem, GachaLogServiceMetadataContext context)
    {
        INameQualityAccess nameQuality = context.GetNameQualityByItemId(gachaItem.ItemId);

        return new()
        {
            UIGFGachaType = gachaItem.QueryType,
            GachaType = gachaItem.GachaType,
            ItemId = gachaItem.ItemId,
            Count = 1,
            Time = gachaItem.Time,
            Name = nameQuality.Name,
            ItemType = context.GetItemType(gachaItem),
            RankType = nameQuality.Quality,
            Id = gachaItem.Id,
        };
    }
}
