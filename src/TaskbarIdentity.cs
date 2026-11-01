using System;
using System.IO;
using System.Runtime.InteropServices;

// A constant Windows taskbar identity shared by the installed app, the stable
// starter and shortcuts. This must never contain the release version or a
// versioned executable path.
internal static class TaskbarIdentity {
    internal const string AppId = "Nexorianexoria.BVE6CompatibilityLauncher";

    [DllImport("shell32.dll", CharSet=CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    internal static void IdentifyProcess(){
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
    }

    // PKEY_AppUserModel_ID = {9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey {
        internal Guid formatId;
        internal uint propertyId;
        internal PropertyKey(Guid id,uint pid){formatId=id;propertyId=pid;}
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant {
        internal ushort variantType;
        internal ushort reserved1;
        internal ushort reserved2;
        internal ushort reserved3;
        internal IntPtr pointer;
        internal IntPtr reserved4;
    }
    [DllImport("ole32.dll", PreserveSig=false)]
    private static extern void PropVariantClear(ref PropVariant variant);

    [ComImport,Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile {
        void GetClassID(out Guid id);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string name,uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string name,[MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string name);
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore {
        void GetCount(out uint count);
        void GetAt(uint index,out PropertyKey key);
        void GetValue(ref PropertyKey key,out PropVariant value);
        void SetValue(ref PropertyKey key,ref PropVariant value);
        void Commit();
    }

    internal static void SetShortcutIdentity(string link){
        if(!File.Exists(link))throw new FileNotFoundException("ショートカットが見つかりません。",link);
        object com=null;
        try {
            com=Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046")));
            var file=(IPersistFile)com;
            file.Load(link,2); // STGM_READWRITE; changing a shortcut needs a writable COM stream
            var properties=(IPropertyStore)com;
            var key=new PropertyKey(
                new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),5);
            var value=new PropVariant{variantType=31,
                pointer=Marshal.StringToCoTaskMemUni(AppId)};
            try {
                properties.SetValue(ref key,ref value);
                properties.Commit();
                file.Save(link,true);
            }finally{PropVariantClear(ref value);}
        }finally{if(com!=null)Marshal.FinalReleaseComObject(com);}
    }

    internal static string GetShortcutIdentity(string link){
        object com=null;
        try {
            com=Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046")));
            ((IPersistFile)com).Load(link,0);
            var properties=(IPropertyStore)com;
            var key=new PropertyKey(
                new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),5);
            PropVariant value;
            properties.GetValue(ref key,out value);
            try{return value.variantType==31?Marshal.PtrToStringUni(value.pointer):null;}
            finally{PropVariantClear(ref value);}
        }finally{if(com!=null)Marshal.FinalReleaseComObject(com);}
    }
}
