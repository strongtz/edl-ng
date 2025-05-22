using System.Text.Json.Serialization;
using Qualcomm.EmergencyDownload.Layers.APSS.Firehose.JSON.StorageInfo;

namespace QCEDL.NET.Json;

[JsonSerializable(typeof(Root))]
[JsonSerializable(typeof(StorageInfo))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;