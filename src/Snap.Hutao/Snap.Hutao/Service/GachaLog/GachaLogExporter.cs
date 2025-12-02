// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.Interchange.GachaLog;
using Snap.Hutao.Service.Metadata;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.GachaLog;

using GachaItemExtension = GachaLog.GachaItemExtensions;

[Service(typeof(IGachaLogExporter))]
internal sealed partial class GachaLogExporter : IGachaLogExporter
{
    private readonly IGachaLogRepository gachaLogRepository;
    private readonly IMetadataService metadataService;
    private readonly ITaskContext taskContext;

    [GeneratedConstructor]
    public GachaLogExporter(
        IGachaLogRepository gachaLogRepository,
        IMetadataService metadataService,
        ITaskContext taskContext)
    {
        this.gachaLogRepository = gachaLogRepository;
        this.metadataService = metadataService;
        this.taskContext = taskContext;
    }

    public async ValueTask<UIGF> ExportAsync(GachaArchive archive, CancellationToken token)
    {
        await taskContext.SwitchToBackgroundAsync();

        GachaLogServiceMetadataContext context = await metadataService.GetContextAsync<GachaLogServiceMetadataContext>(token).ConfigureAwait(false);
        ImmutableList<GachaItem> items = gachaLogRepository.GetGachaItemImmutableListByArchiveId(archive.InnerId);
        string uid = archive.Uid;

        return new()
        {
            Info = UIGFInfo.Create(uid),
            List = items
                .Where(gachaItem => gachaItem.GachaType is not (GachaType.NoviceWish or GachaType.GenshinImpactCollaboration))
                .Select(gachaItem => gachaItem.ToUIGFItem(context))
                .ToImmutableList(),
        };
    }
}
