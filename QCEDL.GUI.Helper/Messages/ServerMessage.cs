using ExhaustiveMatching;

namespace QCEDL.GUI.Helper.Messages;

[Closed(typeof(ServerHello))]
public interface IServerMessage : IMessage;

public readonly record struct ServerHello(string Hello) : IServerMessage;