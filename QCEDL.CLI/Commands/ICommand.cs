using System.CommandLine;

namespace QCEDL.CLI.Commands;

public interface ICommand
{
    Command Create();
}