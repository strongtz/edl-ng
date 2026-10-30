using Qualcomm.EmergencyDownload.Layers.APSS.Firehose;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.Xml.Elements;

namespace QCEDL.NET.Tests;

public sealed class QualcommFirehoseStorageInfoTests
{
    [Fact]
    public void GetStorageInfoSupportsBlockCountsAboveInt32MaxValue()
    {
        const ulong totalBlocks = 3_000_000_000;
        var storageInfoLog =
            $"<data><log value='INFO: {{&quot;storage_info&quot;: {{&quot;total_blocks&quot;:{totalBlocks},&quot;block_size&quot;:4096,&quot;page_size&quot;:4096,&quot;num_physical&quot;:1,&quot;manufacturer_id&quot;:1,&quot;serial_num&quot;:1}}}}' /></data>";

        using var transport = new FirehoseEraseRecordingTransport(
            System.Text.Encoding.UTF8.GetBytes(storageInfoLog),
            "<data><response value='ACK' /></data>"u8.ToArray());
        var firehose = new QualcommFirehose(transport);

        var result = firehose.GetStorageInfo(StorageType.Ufs);

        Assert.NotNull(result?.StorageInfo);
        Assert.Equal(totalBlocks, result.StorageInfo.TotalBlocks);
    }
}
