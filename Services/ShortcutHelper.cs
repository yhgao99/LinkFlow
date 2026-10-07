using System;
using System.IO;

namespace LinkFlow.Services;

public record ShortcutInfo(string TargetPath, string Arguments, string IconLocation, string Name);

public static class ShortcutHelper
{
    public static ShortcutInfo? ResolveShortcut(string shortcutPath)
    {
        try
        {
            if (!File.Exists(shortcutPath) || !shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return null;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(shortcutPath);

            var target = (string)sc.TargetPath;
            var args = (string)sc.Arguments;
            var iconLoc = (string)sc.IconLocation;
            var name = Path.GetFileNameWithoutExtension(shortcutPath);

            return new ShortcutInfo(target, args, iconLoc, name);
        }
        catch
        {
            return null;
        }
    }
}
