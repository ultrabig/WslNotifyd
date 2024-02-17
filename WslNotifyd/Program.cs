using System.Diagnostics;

var port = "12345";
var socat = Process.Start(new ProcessStartInfo("socat")
{
    UseShellExecute = false,
    ArgumentList = {
        $"TCP4-LISTEN:{port},reuseaddr,bind=127.0.0.1",
        "UNIX-CLIENT:/run/user/1000/bus",
    },
    RedirectStandardInput = true,
})!;
var notifyd = Process.Start(new ProcessStartInfo("../WslNotifydWin/scripts/runner.sh")
{
    UseShellExecute = false,
    WorkingDirectory = "../WslNotifydWin",
    ArgumentList = {
        $"tcp:port={port},host=127.0.0.1",
        "1000",
    },
    RedirectStandardInput = true,
})!;
var processes = new[] { socat, notifyd };
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("killing children");
    var exceptionList = new List<Exception>();
    foreach (var proc in processes.Reverse())
    {
        try
        {
            proc.Kill(true);
            proc.Dispose();
        }
        catch (Exception ex)
        {
            exceptionList.Add(ex);
        }
    }
    if (exceptionList.Count > 0)
    {
        throw new AggregateException(exceptionList.ToArray());
    }
    Console.WriteLine("done");
};
Task.WaitAny(processes.Select(p => p.WaitForExitAsync()).ToArray());
Console.WriteLine("exiting");
