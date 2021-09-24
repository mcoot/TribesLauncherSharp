using System;

namespace InjectorStandalone
{
    class InjectorStandalone
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: InjectorStandalone <PID> <dllPath>");
                System.Environment.Exit(1);
            }

            int pid = int.Parse(args[0]);
            string dllPath = args[1];

            TribesLauncherSharp.InjectorLauncher.Inject(pid, dllPath);
        }
    }
}
