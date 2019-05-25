using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;

namespace TribesLauncherSharp
{
    public class InjectorLauncher
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress,
            uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, IntPtr dwStackSize, 
            IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32", SetLastError = true, ExactSpelling = true)]
        internal static extern Int32 WaitForSingleObject(IntPtr handle, Int32 milliseconds);

        [DllImport("kernel32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetExitCodeThread(IntPtr hThread, out IntPtr lpExitCode);

        // Necessary process privilege flags
        const int PROCESS_CREATE_THREAD = 0x0002;
        const int PROCESS_QUERY_INFORMATION = 0x0400;
        const int PROCESS_VM_OPERATION = 0x0008;
        const int PROCESS_VM_WRITE = 0x0020;
        const int PROCESS_VM_READ = 0x0010;

        // Memory allocation flags
        const uint MEM_COMMIT = 0x00001000;
        const uint MEM_RESERVE = 0x00002000;
        const uint PAGE_READWRITE = 4;

        public class InjectorException : Exception
        {
            public InjectorException() : base() { }
            public InjectorException(string message) : base(message) { }
            public InjectorException(string message, Exception inner) : base(message, inner) { }
        }

        public class LauncherException : Exception
        {
            public LauncherException() : base() { }
            public LauncherException(string message) : base(message) { }
            public LauncherException(string message, Exception inner) : base(message, inner) { }
        }

        public static int NumProcessesRunning(string processName) => Process.GetProcessesByName(processName).Length;
        public static bool DoesProcessExist(string processName) => NumProcessesRunning(processName) > 0;
        public static bool DoesProcessExist(int processId) {
            try
            {
                Process.GetProcessById(processId);
                return true;
            } catch (ArgumentException)
            {
                return false;
            }
        }

        public static int LaunchGame(string binaryPath, string loginServerHost, string extraArgs = "")
        {
            if (!File.Exists(binaryPath)) throw new LauncherException("Unable to locate game binary");

            Process p = Process.Start(binaryPath, $"-hostx={loginServerHost} {extraArgs}");

            return p.Id;
        }

        #region Target Process Detection
        public class OnProcessStatusEventArgs : EventArgs
        {
            public string ProcessName { get; set; }
            public int ProcessId { get; set; }

            public OnProcessStatusEventArgs(string processName, int processId)
            {
                ProcessName = processName;
                ProcessId = processId;
            }
        }

        public event EventHandler<OnProcessStatusEventArgs> OnTargetProcessLaunched;
        public event EventHandler<OnProcessStatusEventArgs> OnTargetProcessEnded;
        public event EventHandler<UnhandledExceptionEventArgs> OnTargetPollingException;

        private class ProcessTarget
        {
            private bool TargetById { get; }
            private string TargetName { get; }
            private int TargetId { get; }


            public ProcessTarget(int processId)
            {
                TargetById = true;
                TargetId = processId;
                TargetName = null;
            }

            public ProcessTarget(string processName)
            {
                TargetById = false;
                TargetId = 0;
                TargetName = processName;
            }

            public bool TargetExists()
                => (TargetById && DoesProcessExist(TargetId)) || (!TargetById && DoesProcessExist(TargetName));

            public bool IsTarget(Process process)
                => (TargetById && TargetId == process.Id) || (!TargetById && TargetName == process.ProcessName);

            public Process FindTargetProcess()
            {
                if (!TargetExists()) return null;
                if (TargetById)
                {
                    return Process.GetProcessById(TargetId);
                } else
                {
                    var procs = Process.GetProcessesByName(TargetName);
                    return procs.Length > 0 ? procs[0] : null;
                }
            }
        }

        private ProcessTarget Target { get; set; }
        public Process FoundProcess { get; private set; }

        private Timer PollingTimer { get; set; }

        public InjectorLauncher()
        {
            Target = null;

            PollingTimer = new Timer(1000);
            PollingTimer.AutoReset = true;
            PollingTimer.Elapsed += PollingTimer_Tick;
            PollingTimer.Start();
        }

        private void PollingTimer_Tick(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (Target == null) return;

                if (FoundProcess == null && Target.TargetExists())
                {
                    Process proc = Target.FindTargetProcess();
                    if (proc == null) return;
                    FoundProcess = proc;
                    OnProcessStatusEventArgs args = new OnProcessStatusEventArgs(proc.ProcessName, proc.Id);
                    OnTargetProcessLaunched?.Invoke(this, args);
                    return;
                }

                if (FoundProcess != null && !Target.TargetExists())
                {
                    OnProcessStatusEventArgs args = new OnProcessStatusEventArgs(FoundProcess.ProcessName, FoundProcess.Id);
                    FoundProcess = null;

                    OnTargetProcessEnded?.Invoke(this, args);
                }
            } catch (Exception ex)
            {
                if (OnTargetPollingException == null)
                {
                    throw;
                }
                OnTargetPollingException?.Invoke(this, new UnhandledExceptionEventArgs(ex, false));
            }
        }

        public void SetTarget(string processName)
        {
            Target = new ProcessTarget(processName);
        }

        public void SetTarget(int processId)
        {
            Target = new ProcessTarget(processId);
        }

        public void UnsetTarget()
        {
            Target = null;
        }
        #endregion

        #region Injector
        private static IntPtr GetProcessHandle(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
            {
                throw new InjectorException($"No process with name {processName} exists");
            }

            IntPtr handle = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, processes[0].Id);
            if (handle == IntPtr.Zero) throw new InjectorException("Failed to open handle to process");

            return handle;
        }

        private static IntPtr GetProcessHandle(int processId)
        {
            Process process;
            try
            {
                process = Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                throw new InjectorException($"No process with id {processId} exists");
            }

            IntPtr handle = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, false, process.Id);
            if (handle == IntPtr.Zero) throw new InjectorException("Failed to open handle to process");

            return handle;
        }

        private static IntPtr GetLoadLibraryAddr() => GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");

        private static IntPtr WriteDLLNameToProcessMemory(IntPtr handle, string dllName)
        {
            uint nameLength = (uint)((dllName.Length + 1) * Marshal.SizeOf(typeof(char)));
            IntPtr mem = VirtualAllocEx(handle, IntPtr.Zero, nameLength, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (mem == IntPtr.Zero) throw new InjectorException("Failed to allocate process memory");

            if (!WriteProcessMemory(handle, mem, Encoding.Default.GetBytes(dllName), nameLength, out UIntPtr bytesWritten))
            {
                throw new InjectorException("Failed to write DLL name to process memory");
            }

            return mem;
        }

        private static void InjectInternal(IntPtr handle, string dllPath)
        {
            try
            {
                // Check pre-conditions
                if (!File.Exists(dllPath)) throw new InjectorException($"DLL file {dllPath} does not exist");

                // Get the absolute DLL path
                FileInfo fi = new FileInfo(dllPath);
                string fullDllPath = fi.FullName;

                // Write DLL name into the process
                IntPtr nameAddress = WriteDLLNameToProcessMemory(handle, fullDllPath);

                // Create remote thread
                IntPtr remoteThread = CreateRemoteThread(handle, IntPtr.Zero, IntPtr.Zero, GetLoadLibraryAddr(), nameAddress, 0, IntPtr.Zero);
                if (remoteThread == IntPtr.Zero) throw new InjectorException("Failed to create remote thread");

                // Wait for LoadLibrary to return, waiting at most 10 seconds
                long threadResult = WaitForSingleObject(remoteThread, 10 * 1000);
                if (threadResult == 0x00000080 || threadResult == 0x00000102L || threadResult == 0xFFFFFFFF)
                {
                    throw new InjectorException("Remote thread failed to return");
                }
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static void Inject(string processName, string dllPath) => InjectInternal(GetProcessHandle(processName), dllPath);
        public static void Inject(int processId, string dllPath) => InjectInternal(GetProcessHandle(processId), dllPath);
        #endregion
    }
}
