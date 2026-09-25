$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'lock in.exe'
if (-not (Test-Path -LiteralPath $source)) { throw 'lock in.exe is missing' }

$folder = Join-Path $env:LOCALAPPDATA 'Programs\lock in'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$app = Join-Path $folder 'lock in.exe'
Copy-Item -LiteralPath $source -Destination $app -Force

$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\lock in.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $app
$shortcut.WorkingDirectory = $folder
$shortcut.IconLocation = "$app,0"
$shortcut.Description = 'lock in'
$shortcut.Save()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class LockInShortcutId
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid formatId;
        public uint propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort type;
        [FieldOffset(8)] public IntPtr value;
    }

    public static void Set(string shortcutPath, string appId)
    {
        object link = new ShellLink();
        try
        {
            IPersistFile file = (IPersistFile)link;
            file.Load(shortcutPath, 2);
            IPropertyStore properties = (IPropertyStore)link;
            PropertyKey key = new PropertyKey {
                formatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
                propertyId = 5
            };
            PropVariant value = new PropVariant {
                type = 31,
                value = Marshal.StringToCoTaskMemUni(appId)
            };
            try
            {
                properties.SetValue(ref key, ref value);
                properties.Commit();
                file.Save(shortcutPath, true);
            }
            finally { Marshal.FreeCoTaskMem(value.value); }
        }
        finally { Marshal.ReleaseComObject(link); }
    }
}
'@

[LockInShortcutId]::Set($shortcutPath, 'FiftyKFunded.LockIn')
$installedShortcut = (New-Object -ComObject Shell.Application).NameSpace((Split-Path $shortcutPath -Parent)).ParseName((Split-Path $shortcutPath -Leaf))
if ($installedShortcut.ExtendedProperty('System.AppUserModel.ID') -ne 'FiftyKFunded.LockIn') {
    throw 'windows did not save the app id on the start shortcut'
}

$pins = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
if (Test-Path -LiteralPath $pins) {
    foreach ($pin in Get-ChildItem -LiteralPath $pins -File -Filter '*.lnk') {
        if ($shell.CreateShortcut($pin.FullName).TargetPath -eq $app) {
            [LockInShortcutId]::Set($pin.FullName, 'FiftyKFunded.LockIn')
        }
    }
}

Write-Output "installed $app"
