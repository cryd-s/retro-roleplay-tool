using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

// Startet ein Programm in einem eigenen Windows-Job. Jedes Unterprogramm, das der Filter verbietet,
// wird beendet, sobald Windows es meldet. Das betrifft nur Programme, die aus diesem Start hervorgehen,
// nie andere Programme auf dem PC.
public static class GuardedProcess
{
    const int MsgActiveProcessZero = 4, MsgNewProcess = 6;
    const int AssociateCompletionPortInfo = 7;
    const uint CreateSuspended = 0x4, Infinite = 0xFFFFFFFF;
    const int QueryLimited = 0x1000, Terminate = 0x1;

    // Liefert den Fehlercode des Programms. blocked = Anzahl beendeter Unterprogramme.
    public static int Run(string exe, string arguments, Func<string, bool> forbidden, out int blocked)
    {
        blocked = 0;
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception();
        IntPtr port = IntPtr.Zero;
        var pi = new ProcessInformation();
        try
        {
            port = CreateIoCompletionPort(new IntPtr(-1), IntPtr.Zero, UIntPtr.Zero, 1);
            if (port == IntPtr.Zero) throw new Win32Exception();
            var link = new AssociatePort { CompletionKey = IntPtr.Zero, CompletionPort = port };
            if (!SetInformationJobObject(job, AssociateCompletionPortInfo, ref link, Marshal.SizeOf(typeof(AssociatePort))))
                throw new Win32Exception();

            // Angehalten starten, damit kein Unterprogramm vor der Aufnahme in den Job entstehen kann.
            var si = new StartupInfo();
            si.cb = Marshal.SizeOf(typeof(StartupInfo));
            var commandLine = new StringBuilder("\"" + exe + "\" " + arguments);
            if (!CreateProcess(exe, commandLine, IntPtr.Zero, IntPtr.Zero, false, CreateSuspended, IntPtr.Zero, null, ref si, out pi))
                throw new Win32Exception();
            if (!AssignProcessToJobObject(job, pi.Process))
            {
                var error = new Win32Exception();
                TerminateProcess(pi.Process, 1);
                throw error;
            }
            ResumeThread(pi.Thread);

            DateTime? exitedAt = null;
            while (true)
            {
                uint message;
                UIntPtr key;
                IntPtr data;
                if (GetQueuedCompletionStatus(port, out message, out key, out data, 100))
                {
                    if (message == MsgNewProcess && data.ToInt64() != pi.ProcessId && StopIfForbidden((int)data.ToInt64(), forbidden))
                        blocked++;
                    if (message == MsgActiveProcessZero) break;
                }
                if (exitedAt == null && WaitForSingleObject(pi.Process, 0) == 0) exitedAt = DateTime.UtcNow;
                // Nach dem Ende noch kurz nachsehen, ob zuletzt gestartete Programme gemeldet werden.
                if (exitedAt != null && DateTime.UtcNow - exitedAt.Value > TimeSpan.FromSeconds(3)) break;
            }
            WaitForSingleObject(pi.Process, Infinite);
            int code;
            GetExitCodeProcess(pi.Process, out code);
            return code;
        }
        finally
        {
            if (pi.Thread != IntPtr.Zero) CloseHandle(pi.Thread);
            if (pi.Process != IntPtr.Zero) CloseHandle(pi.Process);
            if (port != IntPtr.Zero) CloseHandle(port);
            CloseHandle(job);   // ohne „beenden beim Schließen“: gestartete erlaubte Programme laufen weiter
        }
    }

    static bool StopIfForbidden(int pid, Func<string, bool> forbidden)
    {
        IntPtr process = OpenProcess(QueryLimited | Terminate, false, pid);
        if (process == IntPtr.Zero) return false;
        try
        {
            var name = new StringBuilder(1024);
            int size = name.Capacity;
            string image = QueryFullProcessImageName(process, 0, name, ref size) ? name.ToString() : null;
            return image != null && forbidden(image) && TerminateProcess(process, 1);
        }
        finally { CloseHandle(process); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AssociatePort { public IntPtr CompletionKey, CompletionPort; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo
    {
        public int cb;
        public string Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public short ShowWindow, Reserved2;
        public IntPtr Reserved2Ptr, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateIoCompletionPort(IntPtr file, IntPtr existingPort, UIntPtr key, uint threads);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref AssociatePort info, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetQueuedCompletionStatus(IntPtr port, out uint bytes, out UIntPtr key, out IntPtr overlapped, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation info);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern int ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(IntPtr process, out int code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool TerminateProcess(IntPtr process, int code);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr handle);
}
