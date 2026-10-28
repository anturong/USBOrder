using System.ServiceProcess;

namespace UsbDriveManager.Service;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--console", StringComparison.OrdinalIgnoreCase)))
        {
            using var host = new ServiceHost(console: true);
            host.Start();

            Console.WriteLine("控制台模式运行中：插拔 U 盘观察输出，Ctrl+C 退出。");

            using var stop = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                stop.Set();
            };

            stop.Wait();
            return 0;
        }

        ServiceBase.Run(new UsbDriveWindowsService());
        return 0;
    }
}
