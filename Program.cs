using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fiddler;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. 参数校验
        if (args.Length < 2) return;

        string urlPattern = args[0];
        string forwardToUrl = args[1];
        int listenPort = (args.Length >= 3) ? int.Parse(args[2]) : 8888;

        // 2. 核心抓包与内存优化逻辑
        // 注意：4.6.2 版本使用 AfterSessionComplete
        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            try
            {
                // 检查 URL 是否匹配正则
                if (Regex.IsMatch(session.fullUrl, urlPattern, RegexOptions.IgnoreCase))
                {
                    // 4.6.2 判断响应是否存在的标准写法
                    if (session.oResponse != null && session.oResponse.MIMEType.Contains("json"))
                    {
                        string jsonBody = session.GetResponseBodyAsString();
                        
                        // 异步转发
                        Task.Run(async () => {
                            try {
                                using (var client = new HttpClient())
                                {
                                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                                    await client.PostAsync(forwardToUrl, content);
                                }
                            } catch { } // 忽略转发异常
                        });
                    }
                }
            }
            finally
            {
                // --- 4.6.2 版本的内存回收秘籍 ---
                // 不使用 Dispose，而是清空大对象，让垃圾回收器(GC)能快速回收
                session.RequestBody = new byte[0];
                session.ResponseBody = new byte[0];
            }
        };

        // 3. 启动参数
        // 4.6.2 不支持 IgnoreServerCertErrors 这种写法，直接在 flags 里定义
        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default 
                                        | FiddlerCoreStartupFlags.RegisterAsSystemProxy 
                                        | FiddlerCoreStartupFlags.DecryptSSL;

        FiddlerApplication.Startup(listenPort, flags);
        
        // 4. 后台长效运行逻辑
        // 在影刀隐藏运行时，通过这个死循环保持进程不退出
        while (true)
        {
            await Task.Delay(60000); // 每分钟休眠一次，极低 CPU 占用
        }
    }
}
