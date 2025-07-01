using System.Threading.Channels;
using QCEDL.GUI.Helper.Messages;

namespace QCEDL.GUI.Helper;

public interface IHelperLauncher
{
    ChannelWriter<IClientMessage> Sender { get; }
    ChannelReader<IServerMessage> Receiver { get; }
}