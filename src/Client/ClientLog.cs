namespace KTable.Client;

public sealed class ClientLog
{
    public Action<string> Print = _ => { };
    public Action<string> Warning = _ => { };
    public Action<string> Error = _ => { };
    public Action<string> Tell = _ => { };
}
