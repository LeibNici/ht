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
        private const string U8LoginProgId = "U8Login.clsLogin";

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
            object login = null;
            try
            {
                login = CreateLogin();
                bool ok = InvokeLogin(login, options);
                if (!ok)
                {
                    Console.Error.WriteLine("U8 登录失败");
                    return 1;
                }

                Console.WriteLine("U8 登录成功");
                Console.WriteLine("ShareString: " + GetShareString(login));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("异常：" + DescribeException(ex));
                return 1;
            }
            finally
            {
                ReleaseCom(login);
                PauseIfNeeded(options == null || options.Pause);
            }
        }

        private static object CreateLogin()
        {
            Type loginType = Type.GetTypeFromProgID(U8LoginProgId, false);
            if (loginType == null)
            {
                throw new COMException("当前 Windows 环境未注册 " + U8LoginProgId);
            }

            return Activator.CreateInstance(loginType);
        }

        private static bool InvokeLogin(object login, U8Options options)
        {
            object result = login.GetType().InvokeMember("Login", BindingFlags.InvokeMethod, null, login, options.LoginArgs);
            return result is bool && (bool)result;
        }

        private static string GetShareString(object login)
        {
            object value = login.GetType().InvokeMember("ShareString", BindingFlags.GetProperty, null, login, null);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string DescribeException(Exception ex)
        {
            TargetInvocationException invocation = ex as TargetInvocationException;
            if (invocation != null && invocation.InnerException != null)
            {
                return invocation.InnerException.Message;
            }

            return ex.GetBaseException().Message;
        }

        private static void ReleaseCom(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
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
            Console.WriteLine("可选：--login-date yyyy-MM-dd --sub-id AS --serial <序列号> --account-id (default)@003 --no-pause");
            Console.WriteLine("也支持环境变量：U8_SERVER, U8_USER, U8_PASSWORD, U8_ACCOUNT, U8_ACCOUNT_ID, U8_YEAR, U8_LOGIN_DATE, U8_SUB_ID, U8_SERIAL");
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
        public string AccountId { get; private set; }
        public string Year { get; private set; }
        public string LoginDate { get; private set; }
        public string SubId { get; private set; }
        public string Serial { get; private set; }
        public bool Pause { get; private set; }

        public object[] LoginArgs
        {
            get { return new object[] { SubId, AccountId, Year, User, Password, LoginDate, Server, Serial }; }
        }

        public static bool TryCreate(string[] args, out U8Options options, out string error)
        {
            Dictionary<string, string> values = ParseArgs(args);
            string account = GetValue(values, "account", "U8_ACCOUNT");
            options = new U8Options
            {
                Server = GetValue(values, "server", "U8_SERVER"),
                User = GetValue(values, "user", "U8_USER"),
                Password = GetValue(values, "password", "U8_PASSWORD", string.Empty),
                Account = account,
                AccountId = GetValue(values, "account-id", "U8_ACCOUNT_ID") ?? FormatAccountId(account),
                Year = GetValue(values, "year", "U8_YEAR"),
                LoginDate = GetValue(values, "login-date", "U8_LOGIN_DATE", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                SubId = GetValue(values, "sub-id", "U8_SUB_ID", "AS"),
                Serial = GetValue(values, "serial", "U8_SERIAL", string.Empty),
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

        private static string FormatAccountId(string account)
        {
            if (string.IsNullOrWhiteSpace(account) || account.IndexOf("@", StringComparison.Ordinal) >= 0)
            {
                return account;
            }

            return "(default)@" + account;
        }

        private static string Validate(U8Options options)
        {
            if (string.IsNullOrWhiteSpace(options.Server)) return "缺少服务器：--server 或 U8_SERVER";
            if (string.IsNullOrWhiteSpace(options.User)) return "缺少账号：--user 或 U8_USER";
            if (string.IsNullOrWhiteSpace(options.Account)) return "缺少账套：--account 或 U8_ACCOUNT";
            if (string.IsNullOrWhiteSpace(options.AccountId)) return "缺少账套标识：--account-id 或 U8_ACCOUNT_ID";
            if (string.IsNullOrWhiteSpace(options.Year)) return "缺少年度：--year 或 U8_YEAR";
            if (string.IsNullOrWhiteSpace(options.LoginDate)) return "缺少登录日期：--login-date 或 U8_LOGIN_DATE";
            return null;
        }
    }
}
