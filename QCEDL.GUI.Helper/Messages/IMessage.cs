using System.Text.Json.Serialization;

namespace QCEDL.GUI.Helper.Messages;

[JsonPolymorphic]
[JsonDerivedType(typeof(ClientHello), "clientHello")]
[JsonDerivedType(typeof(ClientTerminate), "clientTerminate")]
[JsonDerivedType(typeof(ServerHello), "serverHello")]
public interface IMessage;