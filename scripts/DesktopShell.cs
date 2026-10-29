using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AdCDesktop {
    public static class NativeShell {
        [StructLayout(LayoutKind.Sequential)] private struct APPBARDATA {
            public uint Size; public IntPtr Window; public uint Callback; public uint Edge;
            public int Left, Top, Right, Bottom; public IntPtr Parameter;
        }
        private delegate bool EnumCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string cls,string title);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int count);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr w,string l,uint flags,uint timeout,out IntPtr result);
        [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message,ref APPBARDATA data);
        [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] private static extern bool SystemParametersInfo(uint action,uint parameter,string value,uint flags);
        private static APPBARDATA Data() {
            APPBARDATA value=new APPBARDATA(); value.Size=(uint)Marshal.SizeOf(typeof(APPBARDATA)); value.Window=FindWindow("Shell_TrayWnd",null);
            if(value.Window==IntPtr.Zero) throw new InvalidOperationException("Windows taskbar is not available in this desktop session."); return value;
        }
        public static uint TaskbarState() { APPBARDATA data=Data(); return (uint)SHAppBarMessage(4,ref data).ToUInt64(); }
        public static void SetTaskbarAutoHide(bool enabled) {
            uint current=TaskbarState(); uint desired=enabled?(current|1u):(current&~1u);
            if(current!=desired) { APPBARDATA data=Data(); data.Parameter=new IntPtr((long)desired); SHAppBarMessage(10,ref data); }
            if(!enabled) EnumWindows(delegate(IntPtr h,IntPtr p) { StringBuilder name=new StringBuilder(256);GetClassName(h,name,name.Capacity);if(name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd")ShowWindowAsync(h,8);return true;},IntPtr.Zero);
        }
        public static void RefreshIcons(bool visible) {
            IntPtr result; SendMessageTimeout(new IntPtr(0xffff),26,IntPtr.Zero,"Software\\Microsoft\\Windows\\CurrentVersion\\Explorer",2,250,out result);
            EnumWindows(delegate(IntPtr h,IntPtr p) {
                StringBuilder name=new StringBuilder(256); GetClassName(h,name,name.Capacity);
                if(name.ToString()=="Progman"||name.ToString()=="WorkerW") { IntPtr view=FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null); if(view!=IntPtr.Zero) { ShowWindowAsync(view,visible?8:0);IntPtr list=FindWindowEx(view,IntPtr.Zero,"SysListView32",null);if(list!=IntPtr.Zero)ShowWindowAsync(list,visible?8:0); } } return true;
            },IntPtr.Zero);
        }
        public static void SetWallpaper(string path) { if(!SystemParametersInfo(20,0,path,3)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
    }
}
