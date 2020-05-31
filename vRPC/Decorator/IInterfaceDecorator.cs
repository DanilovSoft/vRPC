using System.Diagnostics.CodeAnalysis;

namespace DanilovSoft.vRPC.Decorator
{
    public interface IInterfaceDecorator<out TIface> where TIface : class
    {
        TIface? Proxy { get; }
        string? ControllerName { get; }
    }
}
