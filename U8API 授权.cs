using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace U8ApiAuthCheck
{
    internal static class Program
    {
        private static readonly string[] U8AppProgIds = { "U8Login.U8App", "U8API.U8App" };
        private static readonly string[] U8AppTypeNames = { "U8Login.U8App, U8Login", "U8API.U8App, U8API" };

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
            AddFrameworkDllDirectory();

            U8Options options;
            string error;
            if (!U8Options.TryCreate(args, out options, out error))
            {
                Console.Error.WriteLine(error);
                PrintUsage();
                PauseIfNeeded(true);
                return 2;
            }

            return RunCheck(options);
        }

        private static int RunCheck(U8Options options)
        {
            object u8 = null;
            try
            {
                u8 = CreateU8App();
                int ret = Convert.ToInt32(Invoke(u8, "Login", options.LoginArgs), CultureInfo.InvariantCulture);
                if (ret != 0)
                {
                    Console.Error.WriteLine("登录失败：" + ret);
                    return 1;
                }

                Console.WriteLine("登录成功");
                object bill = Invoke(u8, "GetBill", options.BillArgs);
                Console.WriteLine(bill == null ? "获取接口失败：无 API 授权或接口不存在" : "获取接口成功：有 API 授权");
                return bill == null ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("异常：" + Unwrap(ex).Message);
                return 1;
            }
            finally
            {
                Logout(u8);
                PauseIfNeeded(options == null || options.Pause);
            }
        }

        private static object CreateU8App()
        {
            foreach (string progId in U8AppProgIds)
            {
                Type type = Type.GetTypeFromProgID(progId, false);
                if (type != null)
                {
                    return Activator.CreateInstance(type);
                }
            }

            foreach (string typeName in U8AppTypeNames)
            {
                Type type = Type.GetType(typeName, false);
                if (type != null)
                {
                    return Activator.CreateInstance(type);
                }
            }

            throw new InvalidOperationException("未找到 U8App。请在运行机器安装并注册 U8 客户端/API 组件。");
        }

        private static object Invoke(object target, string methodName, object[] args)
        {
            return target.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod,
                null,
                target,
                args,
                CultureInfo.InvariantCulture);
        }

        private static Exception Unwrap(Exception ex)
        {
            TargetInvocationException invocation = ex as TargetInvocationException;
            COMException com = ex as COMException;
            if (invocation != null && invocation.InnerException != null)
            {
                return invocation.InnerException;
            }

            return com ?? ex;
        }

        private static void Logout(object u8)
        {
            if (u8 == null)
            {
                return;
            }

            try
            {
                Invoke(u8, "Logout", new object[0]);
            }
            catch
            {
            }
        }

        private static void AddFrameworkDllDirectory()
        {
            string frameworkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "U8APIFramework");
            if (Directory.Exists(frameworkDir))
            {
                SetDllDirectory(frameworkDir);
            }
        }

        private static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
        {
            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            foreach (string dir in ProbeDirectories())
            {
                string path = Path.Combine(dir, assemblyName);
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }

            return null;
        }

        private static IEnumerable<string> ProbeDirectories()
        {
            yield return AppDomain.CurrentDomain.BaseDirectory;
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "U8APIFramework");

            string configured = Environment.GetEnvironmentVariable("U8_API_DLL_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                yield return configured;
            }
        }

        private static void PauseIfNeeded(bool pause)
        {
            if (!pause || Console.IsInputRedirected)
            {
                return;
            }

            Console.WriteLine("按 Enter 退出...");
            Console.ReadLine();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法：U8ApiAuthCheck.exe --server <服务器> --user <账号> --password <密码> --account <账套> --year <年度>");
            Console.WriteLine("可选：--module IA --bill PurIn --no-pause");
            Console.WriteLine("也支持环境变量：U8_SERVER, U8_USER, U8_PASSWORD, U8_ACCOUNT, U8_YEAR, U8_BILL_MODULE, U8_BILL_TYPE");
        }

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);
    }

    internal sealed class U8Options
    {
        public string Server { get; private set; }
        public string User { get; private set; }
        public string Password { get; private set; }
        public string Account { get; private set; }
        public string Year { get; private set; }
        public string Module { get; private set; }
        public string Bill { get; private set; }
        public bool Pause { get; private set; }

        public object[] LoginArgs
        {
            get { return new object[] { Server, User, Password, Account, Year, LoginModeValue }; }
        }

        public object[] BillArgs
        {
            get { return new object[] { Module, Bill }; }
        }

        private const int LoginModeValue = 0;

        public static bool TryCreate(string[] args, out U8Options options, out string error)
        {
            Dictionary<string, string> values = ParseArgs(args);
            options = new U8Options
            {
                Server = GetValue(values, "server", "U8_SERVER"),
                User = GetValue(values, "user", "U8_USER"),
                Password = GetValue(values, "password", "U8_PASSWORD"),
                Account = GetValue(values, "account", "U8_ACCOUNT"),
                Year = GetValue(values, "year", "U8_YEAR"),
                Module = GetValue(values, "module", "U8_BILL_MODULE", "IA"),
                Bill = GetValue(values, "bill", "U8_BILL_TYPE", "PurIn"),
                Pause = !values.ContainsKey("no-pause")
            };

            error = Validate(options);
            return string.IsNullOrEmpty(error);
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                string key = NormalizeKey(args[i]);
                if (key == null)
                {
                    continue;
                }

                string value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
                values[key] = value;
            }

            return values;
        }

        private static string NormalizeKey(string arg)
        {
            return arg.StartsWith("--", StringComparison.Ordinal) ? arg.Substring(2) : null;
        }

        private static string GetValue(Dictionary<string, string> values, string key, string envName, string defaultValue = null)
        {
            string value;
            if (values.TryGetValue(key, out value))
            {
                return value;
            }

            return Environment.GetEnvironmentVariable(envName) ?? defaultValue;
        }

        private static string Validate(U8Options options)
        {
            if (string.IsNullOrWhiteSpace(options.Server)) return "缺少服务器：--server 或 U8_SERVER";
            if (string.IsNullOrWhiteSpace(options.User)) return "缺少账号：--user 或 U8_USER";
            if (string.IsNullOrWhiteSpace(options.Password)) return "缺少密码：--password 或 U8_PASSWORD";
            if (string.IsNullOrWhiteSpace(options.Account)) return "缺少账套：--account 或 U8_ACCOUNT";
            if (string.IsNullOrWhiteSpace(options.Year)) return "缺少年度：--year 或 U8_YEAR";
            return null;
        }
    }
}
