using ExhaustiveMatching;

namespace QCEDL.GUI.Helper.Messages;

[Closed(
    typeof(ClientHello),
    typeof(ClientTerminate))]
public interface IClientMessage : IMessage;

public readonly record struct ClientHello(string Hello) : IClientMessage;
public readonly record struct ClientTerminate : IClientMessage;