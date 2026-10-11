using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Win32;

public static class StartupHook
{
    static readonly string LogPath = Environment.GetEnvironmentVariable("SPT_PROBE_LOG")
        ?? Path.Combine(Path.GetTempPath(), "spt-launcher-probe.log");

    static readonly object Gate = new object();

    static void W(string s)
    {
        lock (Gate)
        {
            try { File.AppendAllText(LogPath, s + Environment.NewLine, Encoding.UTF8); } catch { }
        }
    }

    public static void Initialize()
    {
        try { File.WriteAllText(LogPath, "=== SPT launcher probe " + DateTime.Now.ToString("s") + " ===" + Environment.NewLine, Encoding.UTF8); } catch { }
        W("hook Initialize on thread " + Thread.CurrentThread.ManagedThreadId);

        Probe("immediate");
        var t = new Thread(() => { Thread.Sleep(9000); Probe("delayed+9s"); }) { IsBackground = true };
        t.Start();
    }

    static void Probe(string phase)
    {
        W("");
        W("################ phase: " + phase + " ################");

        W("--- registry views ---");
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var sub in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov",
                @"SOFTWARE\Wow6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (var k = baseKey.OpenSubKey(sub))
                    {
                        if (k == null) { W("  REG[" + view + "] HKLM\\" + sub + " : MISSING"); continue; }
                        var vals = k.GetValueNames();
                        if (vals.Length == 0) W("  REG[" + view + "] HKLM\\" + sub + " : key exists, no values");
                        foreach (var vn in vals) W("  REG[" + view + "] HKLM\\" + sub + " :: " + vn + " = " + k.GetValue(vn));
                    }
                }
                catch (Exception e) { W("  REG[" + view + "] " + sub + " ERR " + e.GetType().Name + ": " + e.Message); }
            }
        }

        var asmNames = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).OrderBy(x => x).ToList();
        W("--- loaded assemblies (" + asmNames.Count + ") ---");
        W(string.Join(", ", asmNames));

        var candidates = new List<string> { "SPT.Launcher.Base", "SPT.Launcher", "SPT.Launcher.Core" };
        var loaded = new List<Assembly>();
        foreach (var n in candidates)
        {
            try { var a = Assembly.Load(n); loaded.Add(a); W("Assembly.Load(" + n + ") OK -> " + a.FullName); }
            catch (Exception e) { W("Assembly.Load(" + n + ") failed: " + e.GetType().Name + " " + e.Message); }
        }

        Type util = null, starter = null;
        foreach (var a in loaded)
        {
            Type[] types;
            try { types = a.GetTypes(); } catch (ReflectionTypeLoadException rtle) { types = rtle.Types.Where(x => x != null).ToArray(); }
            foreach (var ty in types)
            {
                if (ty.Name == "ValidationUtil") util = ty;
                if (ty.Name == "GameStarter") starter = ty;
            }
        }
        W("ValidationUtil: " + (util == null ? "<null>" : util.FullName + "  asm=" + util.Assembly.GetName().Name));
        W("GameStarter   : " + (starter == null ? "<null>" : starter.FullName + "  asm=" + starter.Assembly.GetName().Name));

        if (starter != null)
        {
            W("--- GameStarter.DetectOriginalGamePath() ---");
            var m = starter.GetMethod("DetectOriginalGamePath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m != null)
            {
                try { var r = m.Invoke(null, null); W("  returned: " + (r == null ? "<null>" : "'" + r + "'")); }
                catch (Exception e) { W("  THREW: " + (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message); }
            }
            else W("  method not found");
            DumpStrings(starter, "DetectOriginalGamePath");
            DumpStrings(starter, "IsInstalledInLive");
            DumpStrings(starter, "SetupGameFiles");
        }

        if (util != null)
        {
            W("--- ValidationUtil.Validate() ---");
            var m = util.GetMethod("Validate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m != null)
            {
                try { var r = m.Invoke(null, null); W("  returned: " + r); }
                catch (Exception e) { W("  THREW: " + (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message); }
            }
            else W("  method not found");

            var ma = util.GetMethod("a", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (ma != null)
            {
                try { var r = ma.Invoke(null, null); W("  ValidationUtil.a() returned: " + (r == null ? "<null>" : "'" + r + "'")); }
                catch (Exception e) { W("  a() THREW: " + (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message); }
            }

            foreach (var nm in new[] { "Validate", "a", "b", "g", "d" }) DumpStrings(util, nm);

            W("--- static fields of ValidationUtil ---");
            foreach (var f in util.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                object v = null; string err = null;
                try { v = f.GetValue(null); } catch (Exception e) { err = e.GetType().Name; }
                W("  " + f.FieldType.Name + " " + f.Name + " = " + (err != null ? "<" + err + ">" : (v == null ? "<null>" : "'" + v + "'")));
            }
        }
    }

    /// <summary>
    /// 把方法 IL 里所有 ldstr 的字符串常量解析出来。
    /// 启动器核心做了字符串加密，静态扫描 exe 搜不到这些值，但方法被调用/加载后可以这样读出来。
    /// </summary>
    static void DumpStrings(Type t, string methodName)
    {
        var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        if (m == null) { W("  [" + methodName + "] not found"); return; }
        MethodBody body;
        try { body = m.GetMethodBody(); } catch (Exception e) { W("  [" + methodName + "] body error: " + e.Message); return; }
        if (body == null) { W("  [" + methodName + "] no IL body"); return; }
        var il = body.GetILAsByteArray();
        var mod = m.Module;
        var found = new List<string>();
        for (int i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] == 0x72) // ldstr
            {
                int tok = BitConverter.ToInt32(il, i + 1);
                try { var s = mod.ResolveString(tok); found.Add(s); } catch { }
            }
        }
        W("  [" + methodName + "] " + found.Count + " ldstr -> " + string.Join("  ||  ", found));
    }
}
