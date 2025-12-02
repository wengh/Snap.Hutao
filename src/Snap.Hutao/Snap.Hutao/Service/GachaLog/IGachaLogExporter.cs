// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Entity;
using Snap.Hutao.Model.Interchange.GachaLog;

namespace Snap.Hutao.Service.GachaLog;

internal interface IGachaLogExporter
{
    ValueTask<UIGF> ExportAsync(GachaArchive archive, CancellationToken token);
}
