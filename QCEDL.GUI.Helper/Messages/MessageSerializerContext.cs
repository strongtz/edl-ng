using System.Text.Json.Serialization;

namespace QCEDL.GUI.Helper.Messages;

[JsonSerializable(typeof(IMessage))]
public partial class MessageSerializerContext : JsonSerializerContext;