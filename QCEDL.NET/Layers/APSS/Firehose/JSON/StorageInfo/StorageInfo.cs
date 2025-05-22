using System.Text.Json.Serialization;

namespace Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;

public record Root(
    [property: JsonPropertyName("storage_info")] StorageInfo? StorageInfo);

public sealed record StorageInfo(
    [property: JsonPropertyName("total_blocks")] int TotalBlocks,
    [property: JsonPropertyName("block_size")] int BlockSize,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("num_physical")] int NumPhysical,
    [property: JsonPropertyName("manufacturer_id")] int ManufacturerId,
    [property: JsonPropertyName("serial_num")] long SerialNum,
    [property: JsonPropertyName("fw_version")] string FwVersion = "",
    [property: JsonPropertyName("mem_type")] string MemType = "",
    [property: JsonPropertyName("prod_name")] string ProdName = ""
);